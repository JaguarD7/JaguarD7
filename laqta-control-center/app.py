import asyncio, os, sqlite3, re, secrets, hashlib, base64
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo
import bcrypt, feedparser, httpx
from cryptography.fernet import Fernet
from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel
from starlette.middleware.sessions import SessionMiddleware
BASE=Path(__file__).resolve().parent
DB=BASE/"laqta.db"; SEC=BASE/".laqta_secret"; TZ=ZoneInfo("Asia/Riyadh")
if not SEC.exists(): SEC.write_bytes(secrets.token_bytes(32))
RAW=SEC.read_bytes(); CIPHER=Fernet(base64.urlsafe_b64encode(hashlib.sha256(RAW).digest()))
app=FastAPI(title="LAQTA Control Center")
app.add_middleware(SessionMiddleware,secret_key=hashlib.sha256(RAW+b"session").hexdigest(),max_age=2592000)
app.mount("/static",StaticFiles(directory=BASE/"static"),name="static")
def con():
 x=sqlite3.connect(DB,check_same_thread=False); x.row_factory=sqlite3.Row; return x
def gs(k,d=""):
 c=con(); r=c.execute("select value from settings where key=?",(k,)).fetchone(); c.close(); return r["value"] if r else d
def ss(k,v):
 c=con(); c.execute("insert into settings(key,value) values(?,?) on conflict(key) do update set value=excluded.value",(k,str(v))); c.commit(); c.close()
def enc(s): return CIPHER.encrypt(s.encode()).decode() if s else ""
def dec(s):
 try:return CIPHER.decrypt(s.encode()).decode() if s else ""
 except:return ""
def log(m,l="info"):
 c=con(); c.execute("insert into activity(level,message,created_at) values(?,?,?)",(l,m,datetime.now(TZ).isoformat(timespec="seconds"))); c.commit(); c.close()
def auth(r):
 if not r.session.get("ok"): raise HTTPException(401,"login_required")
def init():
 c=con(); c.executescript("""create table if not exists settings(key text primary key,value text not null);create table if not exists offers(id integer primary key autoincrement,title text,current_price text default '',old_price text default '',code text default '',url text default '',source text default 'manual',status text default 'new',created_at text,posted_at text default '');create table if not exists sources(id integer primary key autoincrement,name text,url text unique,enabled integer default 1,last_checked text default '');create table if not exists activity(id integer primary key autoincrement,level text,message text,created_at text);create table if not exists counters(day text primary key,posts integer default 0);""")
 defs={"admin_hash":"","buffer_key":"","buffer_channel":"","buffer_channel_name":"","automation":"0","interval":"120","max_day":"8","mode":"queue","start":"08:00","end":"23:30","disclosure":"قد نحصل على عمولة من بعض الروابط.","last_post":"","brand":"لقطة | LAQTA"}
 for k,v in defs.items(): c.execute("insert or ignore into settings values(?,?)",(k,v))
 c.commit(); c.close()
class Pwd(BaseModel): password:str
class Settings(BaseModel):
 buffer_key:str|None=None; interval:int=120; max_day:int=8; mode:str="queue"; start:str="08:00"; end:str="23:30"; disclosure:str="قد نحصل على عمولة من بعض الروابط."
class Offer(BaseModel):
 title:str; current_price:str=""; old_price:str=""; code:str=""; url:str=""; source:str="manual"
class Source(BaseModel): name:str; url:str
@app.on_event("startup")
async def up(): init(); asyncio.create_task(loop())
@app.get("/",response_class=HTMLResponse)
async def home(): return (BASE/"static/index.html").read_text(encoding="utf-8")
@app.get("/api/bootstrap")
async def boot(r:Request): return {"setup":not bool(gs("admin_hash")),"auth":bool(r.session.get("ok"))}
@app.post("/api/setup")
async def setup(p:Pwd,r:Request):
 if gs("admin_hash"): raise HTTPException(400,"already_setup")
 if len(p.password)<6: raise HTTPException(400,"min_6_chars")
 ss("admin_hash",bcrypt.hashpw(p.password.encode(),bcrypt.gensalt()).decode()); r.session["ok"]=1; log("تم إنشاء لوحة الإدارة"); return {"ok":1}
@app.post("/api/login")
async def login(p:Pwd,r:Request):
 h=gs("admin_hash")
 if not h or not bcrypt.checkpw(p.password.encode(),h.encode()): raise HTTPException(401,"wrong_password")
 r.session["ok"]=1; return {"ok":1}
@app.post("/api/logout")
async def logout(r:Request): r.session.clear(); return {"ok":1}
@app.get("/api/status")
async def status(r:Request):
 auth(r); today=datetime.now(TZ).date().isoformat(); c=con(); row=c.execute("select posts from counters where day=?",(today,)).fetchone(); new=c.execute("select count(*) n from offers where status='new'").fetchone()["n"]; posted=c.execute("select count(*) n from offers where status='posted'").fetchone()["n"]; c.close()
 return {"running":gs("automation")=="1","buffer":bool(gs("buffer_channel")),"channel":gs("buffer_channel_name"),"today":row["posts"] if row else 0,"new":new,"posted":posted,"last":gs("last_post")}
@app.get("/api/settings")
async def settings(r:Request):
 auth(r); return {"interval":int(gs("interval")),"max_day":int(gs("max_day")),"mode":gs("mode"),"start":gs("start"),"end":gs("end"),"disclosure":gs("disclosure"),"key_saved":bool(gs("buffer_key")),"channel":gs("buffer_channel_name")}
@app.post("/api/settings")
async def saveset(p:Settings,r:Request):
 auth(r)
 for k in ("interval","max_day","mode","start","end","disclosure"): ss(k,getattr(p,k))
 if p.buffer_key: ss("buffer_key",enc(p.buffer_key.strip())); ss("buffer_channel",""); ss("buffer_channel_name","")
 log("تم حفظ الإعدادات"); return {"ok":1}
async def bgql(query,variables=None):
 key=dec(gs("buffer_key"))
 if not key: raise RuntimeError("أدخل Buffer API Key أولاً")
 async with httpx.AsyncClient(timeout=25) as x:
  rr=await x.post("https://api.buffer.com",headers={"Authorization":f"Bearer {key}","Content-Type":"application/json"},json={"query":query,"variables":variables or {}})
  if rr.status_code==401: raise RuntimeError("Buffer رفض المفتاح (401)")
  rr.raise_for_status(); j=rr.json()
  if j.get("errors"): raise RuntimeError(j["errors"][0].get("message","Buffer API error"))
  return j.get("data",{})
async def discover():
 qs=["""query { channels { id name service } }""","""query { channels { id name service { name } } }""","""query { account { channels { id name service } } }"""]; last=None
 for q in qs:
  try:
   d=await bgql(q); chans=d.get("channels") or (d.get("account") or {}).get("channels") or []
   if chans:
    ch=next((z for z in chans if "twitter" in str(z).lower() or '"x"' in str(z).lower()),chans[0]); ss("buffer_channel",ch.get("id","")); ss("buffer_channel_name",ch.get("name") or str(ch.get("service","Buffer"))); return ch
  except Exception as e:last=e
 raise RuntimeError(str(last or "لم أجد قناة في Buffer"))
@app.post("/api/buffer/test")
async def btest(r:Request):
 auth(r)
 try: ch=await discover(); log("تم الاتصال بـ Buffer"); return {"ok":1,"channel":ch}
 except Exception as e: log(str(e),"error"); raise HTTPException(400,str(e))
def compose(o):
 parts=[f"🔥 {o['title'].strip()}"]; cp=o["current_price"].strip(); old=o["old_price"].strip(); code=o["code"].strip(); url=o["url"].strip()
 if cp and old: parts.append(f"السعر الآن: {cp} بدل {old} 💸")
 elif cp: parts.append(f"السعر: {cp} 💸")
 if code: parts.append(f"🎟️ كود الخصم: {code}")
 if url: parts.append(f"🔗 {url}")
 d=gs("disclosure").strip()
 if d: parts.append(d)
 parts.append("#عروض #خصومات #السعودية"); return "\n".join(parts)[:275]
async def publish_text(text):
 cid=gs("buffer_channel")
 if not cid: await discover(); cid=gs("buffer_channel")
 attempts=[("""mutation CreatePost($input: CreatePostInput!) { createPost(input:$input) { id } }""",{"input":{"channelId":cid,"text":text,"mode":gs("mode")}}),("""mutation CreatePost($input: PostInput!) { createPost(input:$input) { id } }""",{"input":{"channelId":cid,"text":text}}),("""mutation CreateUpdate($input: CreateUpdateInput!) { createUpdate(input:$input) { id } }""",{"input":{"profileId":cid,"text":text,"now":gs("mode")=="now"}})]; last=None
 for q,v in attempts:
  try:return await bgql(q,v)
  except Exception as e:last=e
 raise RuntimeError(str(last or "تعذر إنشاء المنشور عبر Buffer"))
async def publish_one(force=False):
 now=datetime.now(TZ); day=now.date().isoformat(); c=con(); n=c.execute("select posts from counters where day=?",(day,)).fetchone(); used=n["posts"] if n else 0
 if not force and used>=int(gs("max_day")): c.close(); return "وصل الحد اليومي"
 o=c.execute("select * from offers where status='new' order by id asc limit 1").fetchone()
 if not o: c.close(); return "لا توجد عروض جاهزة"
 try:
  await publish_text(compose(o)); t=now.isoformat(timespec="seconds"); c.execute("update offers set status='posted',posted_at=? where id=?",(t,o["id"])); c.execute("insert into counters(day,posts) values(?,1) on conflict(day) do update set posts=posts+1",(day,)); c.commit(); ss("last_post",t); log("تم إرسال عرض إلى Buffer: "+o["title"]); return "تم"
 finally:c.close()
@app.post("/api/start")
async def start(r:Request):
 auth(r)
 if not gs("buffer_channel"):
  try: await discover()
  except Exception as e: raise HTTPException(400,str(e))
 ss("automation","1"); log("بدأ التشغيل التلقائي"); return {"ok":1}
@app.post("/api/stop")
async def stop(r:Request): auth(r); ss("automation","0"); log("تم إيقاف التشغيل"); return {"ok":1}
@app.post("/api/publish")
async def pub(r:Request):
 auth(r)
 try:return {"ok":1,"message":await publish_one(True)}
 except Exception as e: log(str(e),"error"); raise HTTPException(400,str(e))
@app.get("/api/offers")
async def offers(r:Request):
 auth(r); c=con(); a=[dict(x) for x in c.execute("select * from offers order by id desc limit 100")]; c.close(); return a
@app.post("/api/offers")
async def addoffer(p:Offer,r:Request):
 auth(r); c=con(); c.execute("insert into offers(title,current_price,old_price,code,url,source,status,created_at) values(?,?,?,?,?,?,?,?)",(p.title,p.current_price,p.old_price,p.code,p.url,p.source,"new",datetime.now(TZ).isoformat(timespec="seconds"))); c.commit(); c.close(); log("تمت إضافة عرض: "+p.title); return {"ok":1}
@app.delete("/api/offers/{oid}")
async def deloffer(oid:int,r:Request):
 auth(r); c=con(); c.execute("delete from offers where id=?",(oid,)); c.commit(); c.close(); return {"ok":1}
@app.get("/api/sources")
async def sources(r:Request):
 auth(r); c=con(); a=[dict(x) for x in c.execute("select * from sources order by id desc")]; c.close(); return a
@app.post("/api/sources")
async def addsource(p:Source,r:Request):
 auth(r); c=con(); c.execute("insert or ignore into sources(name,url,enabled) values(?,?,1)",(p.name,p.url)); c.commit(); c.close(); return {"ok":1}
@app.delete("/api/sources/{sid}")
async def delsource(sid:int,r:Request):
 auth(r); c=con(); c.execute("delete from sources where id=?",(sid,)); c.commit(); c.close(); return {"ok":1}
async def scan():
 c=con(); srcs=[dict(x) for x in c.execute("select * from sources where enabled=1")]; c.close(); added=0
 for s in srcs:
  try:
   f=feedparser.parse(s["url"])
   for e in f.entries[:15]:
    title=re.sub(r"\s+"," ",getattr(e,"title","")).strip(); link=getattr(e,"link","")
    if not title: continue
    c=con(); ex=c.execute("select 1 from offers where url=? or title=?",(link,title)).fetchone()
    if not ex: c.execute("insert into offers(title,url,source,status,created_at) values(?,?,?,?,?)",(title,link,s["name"],"new",datetime.now(TZ).isoformat(timespec="seconds"))); c.commit(); added+=1
    c.close()
   c=con(); c.execute("update sources set last_checked=? where id=?",(datetime.now(TZ).isoformat(timespec="seconds"),s["id"])); c.commit(); c.close()
  except Exception as e: log(f"فشل المصدر {s['name']}: {e}","error")
 log(f"فحص المصادر: تمت إضافة {added} عناصر"); return added
@app.post("/api/scan")
async def scanapi(r:Request): auth(r); return {"ok":1,"added":await scan()}
@app.get("/api/activity")
async def activity(r:Request):
 auth(r); c=con(); a=[dict(x) for x in c.execute("select * from activity order by id desc limit 80")]; c.close(); return a
async def loop():
 while True:
  try:
   if gs("automation")=="1":
    now=datetime.now(TZ); hm=now.strftime("%H:%M")
    if gs("start")<=hm<=gs("end"):
     last=gs("last_post"); due=True
     if last:
      try: due=(now-datetime.fromisoformat(last)).total_seconds()>=int(gs("interval"))*60
      except: pass
     if due:
      try: await scan()
      except: pass
      try: await publish_one()
      except Exception as e: log("نشر تلقائي: "+str(e),"error")
  except Exception as e: log("Automation: "+str(e),"error")
  await asyncio.sleep(60)

import asyncio, os, sqlite3, re, secrets, hashlib, base64, json
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
CURATED_URL="https://raw.githubusercontent.com/JaguarD7/JaguarD7/laqta-feed/laqta-control-center/curated_offers.json"
BACKUP_URL="https://raw.githubusercontent.com/JaguarD7/JaguarD7/laqta-feed/laqta-control-center/runtime_backup.json"
CRON_TOKEN=os.getenv("LAQTA_CRON_TOKEN","")
MASTER_PHRASE=os.getenv("LAQTA_MASTER_PHRASE","").encode()
if MASTER_PHRASE:
 RAW=hashlib.sha256(MASTER_PHRASE).digest()
else:
 if not SEC.exists(): SEC.write_bytes(secrets.token_bytes(32))
 RAW=SEC.read_bytes()
CIPHER=Fernet(base64.urlsafe_b64encode(hashlib.sha256(RAW).digest()))
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

BACKUP_KEYS=("admin_hash","buffer_key","buffer_org","buffer_channel","buffer_channel_name","automation","interval","max_day","mode","start","end","disclosure","last_post","pulse_last","brand")

def build_encrypted_backup():
 payload={k:gs(k,"") for k in BACKUP_KEYS}
 raw=json.dumps(payload,ensure_ascii=False,separators=(",",":")).encode()
 return CIPHER.encrypt(raw).decode()

async def restore_backup():
 try:
  async with httpx.AsyncClient(timeout=15,follow_redirects=True) as x:
   rr=await x.get(BACKUP_URL,headers={"User-Agent":"LAQTA-Control/1.0"})
  if rr.status_code!=200: return False
  obj=rr.json(); token=str(obj.get("ciphertext","")).strip()
  if not token: return False
  payload=json.loads(CIPHER.decrypt(token.encode()).decode())
  for k in BACKUP_KEYS:
   if k in payload: ss(k,payload[k])
  return True
 except Exception:
  return False
def init():
 c=con(); c.executescript("""create table if not exists settings(key text primary key,value text not null);create table if not exists offers(id integer primary key autoincrement,title text,current_price text default '',old_price text default '',code text default '',url text default '',source text default 'manual',status text default 'new',created_at text,posted_at text default '',score integer default 0);create table if not exists sources(id integer primary key autoincrement,name text,url text unique,enabled integer default 1,last_checked text default '');create table if not exists activity(id integer primary key autoincrement,level text,message text,created_at text);create table if not exists counters(day text primary key,posts integer default 0);""")
 try:
  cols=[r["name"] for r in c.execute("pragma table_info(offers)").fetchall()]
  if "score" not in cols: c.execute("alter table offers add column score integer default 0")
 except: pass
 defs={"admin_hash":"","buffer_key":"","buffer_org":"","buffer_channel":"","buffer_channel_name":"","automation":"0","interval":"120","max_day":"8","mode":"queue","start":"08:00","end":"23:30","disclosure":"قد نحصل على عمولة من بعض الروابط.","last_post":"","pulse_last":"","brand":"لقطة | LAQTA"}
 for k,v in defs.items(): c.execute("insert or ignore into settings values(?,?)",(k,v))
 c.commit(); c.close()
class Pwd(BaseModel): password:str
class Settings(BaseModel):
 buffer_key:str|None=None; interval:int=120; max_day:int=8; mode:str="queue"; start:str="08:00"; end:str="23:30"; disclosure:str="قد نحصل على عمولة من بعض الروابط."
class Offer(BaseModel):
 title:str; current_price:str=""; old_price:str=""; code:str=""; url:str=""; source:str="manual"
class Source(BaseModel): name:str; url:str
@app.on_event("startup")
async def up():
 init()
 await restore_backup()
 asyncio.create_task(loop())
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
 if p.buffer_key: ss("buffer_key",enc(p.buffer_key.strip())); ss("buffer_org",""); ss("buffer_channel",""); ss("buffer_channel_name","")
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
 d=await bgql("""query GetOrganizations { account { organizations { id name ownerEmail } } }""")
 orgs=(d.get("account") or {}).get("organizations") or []
 if not orgs: raise RuntimeError("لم أجد Organization في حساب Buffer")
 org=orgs[0]
 org_id=org.get("id")
 q="""query GetChannels($orgId: OrganizationId!) {
   channels(input: { organizationId: $orgId }) {
     id
     name
     displayName
     service
     isQueuePaused
   }
 }"""
 d=await bgql(q,{"orgId":org_id})
 chans=d.get("channels") or []
 if not chans: raise RuntimeError("لا توجد قنوات Social متصلة داخل Buffer")
 ch=next((z for z in chans if str(z.get("service","")).lower()=="twitter"),None)
 if not ch:
  ch=next((z for z in chans if "twitter" in str(z).lower() or '"x"' in str(z).lower()),None)
 if not ch:
  names=", ".join([f"{z.get('displayName') or z.get('name')} ({z.get('service')})" for z in chans])
  raise RuntimeError("لم أجد قناة X/Twitter في Buffer. القنوات الموجودة: "+names)
 ss("buffer_org",org_id)
 ss("buffer_channel",ch.get("id",""))
 ss("buffer_channel_name",ch.get("displayName") or ch.get("name") or "X")
 return {"organization":org,"channel":ch}
@app.post("/api/buffer/test")
async def btest(r:Request):
 auth(r)
 try: ch=await discover(); log("تم الاتصال بـ Buffer"); return {"ok":1,"channel":ch}
 except Exception as e: log(str(e),"error"); raise HTTPException(400,str(e))
def _money(v):
 s=str(v or "").replace("ر.س","").replace("ريال","").replace(",","").strip()
 m=re.search(r"\d+(?:\.\d+)?",s)
 return float(m.group()) if m else None

def compose(o):
 title=re.sub(r"\s+"," ",o["title"].strip())
 if len(title)>92: title=title[:89].rstrip()+"..."
 cp=o["current_price"].strip(); oldp=o["old_price"].strip(); code=o["code"].strip(); url=o["url"].strip()
 src=(o["source"] or "").lower()
 now=_money(cp); before=_money(oldp)
 hooks={
  "noon":["🔥 لقطة نون اليوم","⚡ سعر يستاهل الوقفة","🎯 لقطة نون سريعة"],
  "temu":["🔥 لقطة Temu اليوم","😮‍💨 سعر Temu ملفت","⚡ لقطة تستاهل تشيكها"],
  "shein":["🔥 لقطة SHEIN اليوم","✨ اختيار يستاهل","⚡ لقطة SHEIN سريعة"],
  "amazon":["🔥 لقطة Amazon اليوم","🎯 اختيار Amazon يستاهل","⚡ سعر ملفت على Amazon"]
 }
 arr=next((v for k,v in hooks.items() if k in src),["🔥 لقطة اليوم","⚡ لقطة سريعة","🎯 اختيار يستاهل"])
 parts=[arr[(sum(map(ord,title))+len(url))%len(arr)],title]
 if now is not None and before is not None and before>now:
  pct=round((before-now)/before*100); parts.append(f"💸 {cp} بدل {oldp} — خصم {pct}%")
 elif cp:
  parts.append(f"💸 {cp}")
 if code:
  if "noon" in src: parts.append(f"🏷️ {code} | خصم 10% حسب شروط نون")
  elif "temu" in src: parts.append(f"🏷️ {code} | الخصم حسب الأهلية والحملة")
  elif "shein" in src: parts.append(f"🏷️ {code} | ابحث بالكود داخل SHEIN")
  else: parts.append(f"🏷️ {code}")
 if url: parts.append(f"👇 {url}")
 parts.append("لقطة | الزبدة بدون لف 🎯")
 d=gs("disclosure").strip()
 if d: parts.append(d)
 text="\n".join(parts)
 return text[:278]
async def publish_text(text):
 cid=gs("buffer_channel")
 if not cid:
  await discover()
  cid=gs("buffer_channel")
 mode="shareNow" if gs("mode")=="now" else "addToQueue"
 q="""mutation CreatePost($input: CreatePostInput!) {
   createPost(input:$input) {
     ... on PostActionSuccess {
       post { id text dueAt status shareMode }
     }
     ... on MutationError {
       message
     }
   }
 }"""
 d=await bgql(q,{"input":{"text":text,"channelId":cid,"schedulingType":"automatic","mode":mode}})
 result=d.get("createPost") or {}
 if result.get("message"): raise RuntimeError(result.get("message"))
 if not result.get("post"): raise RuntimeError("Buffer لم يرجع Post بعد طلب النشر")
 return result.get("post")
async def publish_one(force=False):
 now=datetime.now(TZ); day=now.date().isoformat(); c=con(); n=c.execute("select posts from counters where day=?",(day,)).fetchone(); used=n["posts"] if n else 0
 if not force and used>=int(gs("max_day")): c.close(); return "وصل الحد اليومي"
 o=c.execute("select * from offers where status='new' order by score desc, id asc limit 1").fetchone()
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
 try: await scan()
 except Exception as e: log("فحص البداية: "+str(e),"error")
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
 auth(r); c=con(); c.execute("insert into offers(title,current_price,old_price,code,url,source,status,created_at,score) values(?,?,?,?,?,?,?,?,?)",(p.title,p.current_price,p.old_price,p.code,p.url,p.source,"new",datetime.now(TZ).isoformat(timespec="seconds"),0)); c.commit(); c.close(); log("تمت إضافة عرض: "+p.title); return {"ok":1}
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
async def import_curated():
 added=0
 try:
  async with httpx.AsyncClient(timeout=20,follow_redirects=True) as x:
   rr=await x.get(CURATED_URL,headers={"User-Agent":"LAQTA-Control/1.0"})
   rr.raise_for_status(); data=rr.json()
  items=sorted(data.get("offers",[]),key=lambda z:int(z.get("score",0)),reverse=True)
  for it in items:
   title=re.sub(r"\s+"," ",str(it.get("title",""))).strip()
   url=str(it.get("url","")).strip()
   if not title or not url or int(it.get("score",0))<5: continue
   c=con(); ex=c.execute("select 1 from offers where url=? or title=?",(url,title)).fetchone()
   if not ex:
    c.execute("insert into offers(title,current_price,old_price,code,url,source,status,created_at,score) values(?,?,?,?,?,?,?,?,?)",
      (title,str(it.get("current_price","")),str(it.get("old_price","")),str(it.get("code","")),url,str(it.get("source","auto")),"new",datetime.now(TZ).isoformat(timespec="seconds"),int(it.get("score",0))))
    c.commit(); added+=1
   c.close()
 except Exception as e:
  log("فشل استيراد العروض المختارة: "+str(e),"error")
 return added

async def scan():
 added=await import_curated()
 c=con(); srcs=[dict(x) for x in c.execute("select * from sources where enabled=1")]; c.close()
 for s in srcs:
  try:
   f=feedparser.parse(s["url"])
   for e in f.entries[:15]:
    title=re.sub(r"\s+"," ",getattr(e,"title","")).strip(); link=getattr(e,"link","")
    blob=(title+" "+str(getattr(e,"summary",""))).lower()
    if not title or not link: continue
    if not any(k in blob for k in ("خصم","عرض","off","deal","sale","best seller","الأكثر مبيع")): continue
    c=con(); ex=c.execute("select 1 from offers where url=? or title=?",(link,title)).fetchone()
    if not ex:
     c.execute("insert into offers(title,url,source,status,created_at,score) values(?,?,?,?,?,?)",(title,link,s["name"],"new",datetime.now(TZ).isoformat(timespec="seconds"),3))
     c.commit(); added+=1
    c.close()
   c=con(); c.execute("update sources set last_checked=? where id=?",(datetime.now(TZ).isoformat(timespec="seconds"),s["id"])); c.commit(); c.close()
  except Exception as e: log(f"فشل المصدر {s['name']}: {e}","error")
 log(f"فحص المصادر: تمت إضافة {added} عناصر"); return added
@app.post("/api/scan")
async def scanapi(r:Request): auth(r); return {"ok":1,"added":await scan()}

@app.get("/api/backup")
async def public_backup():
 return {"ciphertext":build_encrypted_backup(),"updated_at":datetime.now(TZ).isoformat(timespec="seconds")}

@app.post("/api/pulse")
async def public_pulse():
 now=datetime.now(TZ)
 last=gs("pulse_last")
 if last:
  try:
   if (now-datetime.fromisoformat(last)).total_seconds()<2700:
    return {"ok":1,"skipped":"rate_limited"}
  except: pass
 ss("pulse_last",now.isoformat(timespec="seconds"))
 if gs("automation")!="1":
  return {"ok":1,"running":False}
 hm=now.strftime("%H:%M")
 if not (gs("start")<=hm<=gs("end")):
  return {"ok":1,"running":True,"window":False}
 added=await scan()
 last_post=gs("last_post"); due=True
 if last_post:
  try: due=(now-datetime.fromisoformat(last_post)).total_seconds()>=int(gs("interval"))*60
  except: pass
 msg="ليس موعد النشر بعد"
 if due:
  try: msg=await publish_one()
  except Exception as e: log("Pulse publish: "+str(e),"error"); msg=str(e)
 return {"ok":1,"running":True,"added":added,"message":msg}

@app.post("/api/cron")
async def cron_tick(r:Request):
 token=r.headers.get("X-Cron-Token","")
 if not CRON_TOKEN or not token or not secrets.compare_digest(token,CRON_TOKEN):
  raise HTTPException(403,"forbidden")
 if gs("automation")!="1": return {"ok":1,"running":False}
 now=datetime.now(TZ); hm=now.strftime("%H:%M")
 if not (gs("start")<=hm<=gs("end")): return {"ok":1,"running":True,"window":False}
 added=await scan()
 last=gs("last_post"); due=True
 if last:
  try: due=(now-datetime.fromisoformat(last)).total_seconds()>=int(gs("interval"))*60
  except: pass
 msg="ليس موعد النشر بعد"
 if due:
  try: msg=await publish_one()
  except Exception as e: log("Cron publish: "+str(e),"error"); msg=str(e)
 return {"ok":1,"running":True,"added":added,"message":msg}
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

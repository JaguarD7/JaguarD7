import json,re,html,urllib.parse,urllib.request,xml.etree.ElementTree as ET
from datetime import datetime,timezone

UA={"User-Agent":"Mozilla/5.0 LAQTA-Deal-Scout/1.0"}
STORES={
 "amazon":{"queries":["site:amazon.sa/dp خصم Amazon السعودية","site:amazon.sa/dp عرض Amazon السعودية","site:amazon.sa/dp best seller Saudi Arabia"],"code":"","fallback":"","tag":"laqtasa06-21"},
 "noon":{"queries":["site:noon.com/saudi-en "Best Seller" "Off"","site:noon.com/saudi-en "Selling out fast" "Off"","site:noon.com/saudi-en "Mega Deal" السعودية"],"code":"LQSA","fallback":"https://s.noon.com/kXLee9Y0nLs"},
 "temu":{"queries":["site:temu.com السعودية منتج خصم Temu","site:temu.com Saudi Arabia deal Temu product","site:temu.com Temu popular product sale"],"code":"alr408026","fallback":"https://temu.to/k/e76r9skmde8"},
 "shein":{"queries":["site:ar.shein.com SHEIN السعودية خصم منتج","site:ar.shein.com SHEIN sale Saudi product","site:ar.shein.com SHEIN best seller Saudi"],"code":"US3RU32","fallback":"https://onelink.shein.com/54/637r0pw8u2hc"}
}
BAD=("help","support","customer service","terms","privacy","affiliate","login","sign in","وظائف","سياسة","مساعدة")
def txt(s): return re.sub(r"\s+"," ",html.unescape(re.sub("<[^>]+>"," ",s or ""))).strip()
def search(q):
 u="https://www.bing.com/search?format=rss&q="+urllib.parse.quote_plus(q)
 req=urllib.request.Request(u,headers=UA)
 with urllib.request.urlopen(req,timeout=20) as r: data=r.read()
 root=ET.fromstring(data)
 out=[]
 for i in root.findall(".//item")[:12]:
  out.append({"title":txt(i.findtext("title")),"url":txt(i.findtext("link")),"desc":txt(i.findtext("description"))})
 return out
def money(s):
 vals=[]
 for pat in [r"(?:SAR|ر\.?\s?س\.?|ريال)\s*([0-9]+(?:[.,][0-9]{1,2})?)",r"([0-9]+(?:[.,][0-9]{1,2})?)\s*(?:SAR|ر\.?\s?س\.?|ريال)"]:
  for x in re.findall(pat,s,re.I):
   try: vals.append(float(x.replace(",","")))
   except: pass
 return vals
def disc(s):
 m=re.search(r"(\d{1,2})\s*%\s*(?:off|خصم)?",s,re.I)
 if not m: m=re.search(r"(?:خصم|off)\s*(\d{1,2})\s*%",s,re.I)
 return int(m.group(1)) if m else 0
def productish(store,url):
 u=url.lower()
 if store=="amazon": return bool(re.search(r"/dp/[A-Z0-9]{10}",url,re.I))
 if store=="shein": return "-p-" in u and ".html" in u
 if store=="temu": return "-g-" in u or "/g-" in u or "temu.to/k/" in u
 if store=="noon": return "/p/" in u or "/product" in u
 return False
def affiliate_url(store,url):
 if store=="amazon":
  m=re.search(r"/dp/([A-Z0-9]{10})",url,re.I)
  if not m:return ""
  return f"https://www.amazon.sa/dp/{m.group(1)}/ref=nosim?tag=laqtasa06-21"
 return url
def score(store,title,desc,url):
 s=(title+" "+desc).lower(); v=0; d=disc(s); prices=money(s)
 if productish(store,url):v+=3
 if prices:v+=2
 if d>=40:v+=5
 elif d>=20:v+=4
 elif d>=10:v+=2
 if any(k in s for k in ("best seller","الأكثر مبيع","selling out fast","sold recently","popular","رائج","mega deal")):v+=3
 if any(k in s for k in BAD):v-=8
 if store=="noon" and any(k in s for k in ("apparel","footwear","bag","jewelry","watch","fragrance","sports","toy","عطر","ملابس","أحذية","شنط")):v+=2
 return v,d,prices
def clean_title(t):
 t=re.sub(r"\s*[-|]\s*(Amazon\.sa|Amazon Saudi Arabia|noon|SHEIN|Temu).*$","",t,flags=re.I)
 return t[:150].strip(" -|")
def build():
 now=datetime.now(timezone.utc).isoformat(timespec="seconds")
 cand=[]
 slot=datetime.now(timezone.utc).hour//4
 for store,cfg in STORES.items():
  q=cfg["queries"][slot%len(cfg["queries"])]
  try: items=search(q)
  except Exception: items=[]
  for it in items:
   u=it["url"]
   if store=="amazon" and "amazon.sa" not in u: continue
   if store=="noon" and "noon.com" not in u: continue
   if store=="temu" and "temu." not in u: continue
   if store=="shein" and "shein." not in u: continue
   sc,d,prices=score(store,it["title"],it["desc"],u)
   if sc<5: continue
   vals=sorted(set(prices))
   cur=""; old=""
   if vals:
    cur=f"{vals[0]:g} ر.س"
    if len(vals)>1 and vals[-1]>vals[0]: old=f"{vals[-1]:g} ر.س"
   au=affiliate_url(store,u)
   if not au: au=cfg["fallback"]
   if not au: continue
   cand.append({"title":clean_title(it["title"]),"current_price":cur,"old_price":old,"code":cfg["code"],"url":au,"source":store,"score":sc,"discount":d,"verified_at":now,"source_url":u})
 # add stable campaigns as fallback so queue never dries up
 fallbacks=[
  {"title":"عروض Temu المختارة اليوم","current_price":"","old_price":"","code":"alr408026","url":"https://temu.to/k/e76r9skmde8","source":"temu","score":6,"discount":0,"verified_at":now,"source_url":"campaign"},
  {"title":"اختيارات SHEIN والعروض الحالية","current_price":"","old_price":"","code":"US3RU32","url":"https://onelink.shein.com/54/637r0pw8u2hc","source":"shein","score":6,"discount":0,"verified_at":now,"source_url":"campaign"},
  {"title":"عروض نون السعودية اليوم","current_price":"","old_price":"","code":"LQSA","url":"https://s.noon.com/kXLee9Y0nLs","source":"noon","score":6,"discount":0,"verified_at":now,"source_url":"campaign"}
 ]
 cand+=fallbacks
 seen=set(); out=[]
 for x in sorted(cand,key=lambda z:z["score"],reverse=True):
  k=(x["source"],x["url"],x["title"].lower())
  if k in seen:continue
  seen.add(k);out.append(x)
  if len(out)>=18:break
 return {"generated_at":now,"offers":out}
if __name__=="__main__":
 data=build()
 with open("laqta-control-center/curated_offers.json","w",encoding="utf-8") as f: json.dump(data,f,ensure_ascii=False,indent=2)
 print("offers",len(data["offers"]))

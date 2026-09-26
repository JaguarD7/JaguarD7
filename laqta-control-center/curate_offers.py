import json, re, html, urllib.parse, urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

UA = {"User-Agent": "Mozilla/5.0 LAQTA-Deal-Scout/1.0"}

STORES = {
    "amazon": {
        "queries": [
            "site:amazon.sa/dp خصم Amazon السعودية",
            "site:amazon.sa/dp عرض Amazon السعودية",
            "site:amazon.sa/dp best seller Saudi Arabia"
        ],
        "code": "",
        "fallback": "",
        "tag": "laqtasa06-21"
    },
    "noon": {
        "queries": [
            "site:noon.com/saudi-en best seller off",
            "site:noon.com/saudi-en mega deal",
            "site:noon.com/saudi-en خصم السعودية"
        ],
        "code": "LQSA",
        "fallback": "https://s.noon.com/kXLee9Y0nLs"
    },
    "temu": {
        "queries": [
            "site:temu.com Saudi Arabia deal product",
            "site:temu.com السعودية خصم منتج",
            "site:temu.com popular product sale"
        ],
        "code": "alr408026",
        "fallback": "https://temu.to/k/e76r9skmde8"
    },
    "shein": {
        "queries": [
            "site:ar.shein.com SHEIN السعودية خصم منتج",
            "site:ar.shein.com SHEIN sale Saudi product",
            "site:ar.shein.com SHEIN best seller Saudi"
        ],
        "code": "US3RU32",
        "fallback": "https://onelink.shein.com/54/637r0pw8u2hc"
    }
}

BLOCKED_PRODUCT_IDS = {"N70105548V"}
BLOCKED_TITLE_PATTERNS = (r"iphone\s*16.*128gb",)

def is_blocked_offer(title, url):
    u=str(url or "").upper(); t=str(title or "").lower()
    if any(x in u for x in BLOCKED_PRODUCT_IDS): return True
    return any(re.search(p,t,re.I) for p in BLOCKED_TITLE_PATTERNS)

BAD = (
    "help", "support", "customer service", "terms", "privacy", "affiliate",
    "login", "sign in", "وظائف", "سياسة", "مساعدة"
)

def txt(s):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", s or ""))).strip()

def search(q):
    url = "https://www.bing.com/search?format=rss&q=" + urllib.parse.quote_plus(q)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=20) as r:
        data = r.read()
    root = ET.fromstring(data)
    out = []
    for i in root.findall(".//item")[:12]:
        out.append({
            "title": txt(i.findtext("title")),
            "url": txt(i.findtext("link")),
            "desc": txt(i.findtext("description")),
        })
    return out

def money(s):
    vals = []
    patterns = [
        r"(?:SAR|ر\.?\s?س\.?|ريال)\s*([0-9]+(?:[.,][0-9]{1,2})?)",
        r"([0-9]+(?:[.,][0-9]{1,2})?)\s*(?:SAR|ر\.?\s?س\.?|ريال)",
    ]
    for pat in patterns:
        for x in re.findall(pat, s, re.I):
            try:
                vals.append(float(x.replace(",", "")))
            except Exception:
                pass
    return vals

def disc(s):
    m = re.search(r"(\d{1,2})\s*%\s*(?:off|خصم)?", s, re.I)
    if not m:
        m = re.search(r"(?:خصم|off)\s*(\d{1,2})\s*%", s, re.I)
    return int(m.group(1)) if m else 0

def productish(store, url):
    u = url.lower()
    if store == "amazon":
        return bool(re.search(r"/dp/[A-Z0-9]{10}", url, re.I))
    if store == "shein":
        return "-p-" in u and ".html" in u
    if store == "temu":
        return "-g-" in u or "/g-" in u or "temu.to/k/" in u
    if store == "noon":
        return "/p/" in u or "/product" in u
    return False

def tracked_url(store, url, cfg):
    if store == "amazon":
        m = re.search(r"/dp/([A-Z0-9]{10})", url, re.I)
        if not m:
            return ""
        return f"https://www.amazon.sa/dp/{m.group(1)}/ref=nosim?tag={cfg['tag']}"
    # For Noon / Temu / SHEIN, the user's referral/coupon code is attached
    # separately in the post. Keep the official product URL so users land
    # on the exact product instead of a mismatched generic page.
    return url

def score(store, title, desc, url):
    blob = (title + " " + desc).lower()
    v = 0
    d = disc(blob)
    prices = money(blob)
    if productish(store, url):
        v += 4
    if prices:
        v += 2
    if d >= 40:
        v += 5
    elif d >= 20:
        v += 4
    elif d >= 10:
        v += 2
    if any(k in blob for k in (
        "best seller", "الأكثر مبيع", "selling out fast", "sold recently",
        "popular", "رائج", "mega deal", "عرض محدود", "limited deal"
    )):
        v += 3
    if any(k in blob for k in BAD):
        v -= 10
    if store == "noon" and any(k in blob for k in (
        "apparel", "footwear", "bag", "jewelry", "watch", "fragrance",
        "sports", "toy", "عطر", "ملابس", "أحذية", "شنط", "ساعات"
    )):
        v += 2
    return v, d, prices

def clean_title(t):
    t = re.sub(r"\s*[-|]\s*(Amazon\.sa|Amazon Saudi Arabia|noon|SHEIN|Temu).*$", "", t, flags=re.I)
    return t[:150].strip(" -|")

def extract_product_title(desc, fallback_title):
    s = txt(desc)
    # Search-result snippets from store category pages often begin with a real product.
    m = re.search(r"(.{18,170}?)\s+[1-5]\.\d\s+(?:\d|[0-9.,]+[Kk])", s)
    if m:
        candidate = re.sub(r"^(?:أفضل المنتجات|عرض الميجا|عروض اليوم الوطني|عرض)\s*[📣🔥]*\s*", "", m.group(1)).strip()
        if len(candidate) >= 18:
            return candidate[:150]
    return clean_title(fallback_title)

def _num_from_price_text(s):
    m=re.search(r"([0-9]+(?:[.,][0-9]{1,2})?)",str(s or "").replace(",",""))
    return float(m.group(1)) if m else None

def fetch_text(url, timeout=25):
    req=urllib.request.Request(url,headers=UA)
    with urllib.request.urlopen(req,timeout=timeout) as r:
        return r.read().decode("utf-8","ignore")

def amazon_direct_deals(now):
    out=[]
    try:
        s=fetch_text("https://www.amazon.sa/deals",30)
    except Exception:
        return out
    seen=set()
    for m in re.finditer(r'href=["\\\']([^"\\\']*/dp/([A-Z0-9]{10})[^"\\\']*)',s,re.I):
        href,asin=m.group(1),m.group(2).upper()
        if asin in seen or asin in BLOCKED_PRODUCT_IDS: continue
        seen.add(asin)
        slug=href.split("/dp/")[0].rsplit("/",1)[-1]
        title=urllib.parse.unquote(slug).replace("-"," ").strip()
        if len(title)<8: title=f"عرض Amazon {asin}"
        start=s.rfind('<div class="a-cardui dcl-product"',0,m.start())
        if start<0: start=max(0,m.start()-1800)
        end=s.find('<div class="a-cardui dcl-product"',m.end())
        if end<0: end=min(len(s),m.end()+3500)
        card=txt(s[start:end])
        dm=re.search(r"خصم\s*(\d{1,2})%",card)
        if not dm: dm=re.search(r"(\d{1,2})%\s*خصم",card)
        d=int(dm.group(1)) if dm else 0
        vals=[]
        for x in re.findall(r"([0-9][0-9,.]*)\s*ريال",card):
            try: vals.append(float(x.replace(",","")))
            except: pass
        vals=[v for v in vals if 0.5<=v<=100000]
        cur=""; old=""
        if vals:
            curv=min(vals[:8]); oldv=max(vals[:8])
            cur=f"{curv:g} ر.س"
            if oldv>curv*1.02: old=f"{oldv:g} ر.س"
        if d<8 and not cur: continue
        scorev=8+(4 if d>=30 else 2 if d>=15 else 0)+(1 if "عرض" in card else 0)
        out.append({
            "title":title[:150],
            "current_price":cur,
            "old_price":old,
            "code":"",
            "url":f"https://www.amazon.sa/dp/{asin}/ref=nosim?tag=laqtasa06-21",
            "source":"amazon",
            "score":scorev,
            "discount":d,
            "verified_at":now,
            "source_url":"https://www.amazon.sa"+html.unescape(href.split("?")[0]),
            "tracking":"amazon_tag"
        })
        if len(out)>=35: break
    return out

def temu_direct_deals(now):
    out=[]
    try:
        s=fetch_text("https://www.temu.com/sa/",30)
    except Exception:
        return out
    bad=("sex","dildo","vibrator","adult toy","جنسي","للبالغين فقط","إباحية")
    seen=set()
    for m in re.finditer(r'"goodsId":"(\d+)","goodsName":"((?:\\.|[^"])*)"',s,re.S):
        gid,name_raw=m.group(1),m.group(2)
        if gid in seen: continue
        seen.add(gid)
        name=name_raw.replace("\\u002F","/").replace("\\u0026","&").replace("\\u0022",'"')
        if any(k.lower() in name.lower() for k in bad): continue
        chunk=s[m.start():m.start()+7000]
        currency=re.search(r'"currency":"([^"]+)"',chunk)
        p=re.search(r'"priceStr":"((?:\\.|[^"])*)"',chunk)
        mp=re.search(r'"marketPriceStr":"((?:\\.|[^"])*)"',chunk)
        sales=re.search(r'"salesTip":"((?:\\.|[^"])*)"',chunk)
        if not currency or currency.group(1)!="SAR" or not p: continue
        price=p.group(1).replace("\\u061c","").replace("\\u002F","/")
        market=(mp.group(1).replace("\\u061c","").replace("\\u002F","/") if mp else "")
        pv=_num_from_price_text(price); mv=_num_from_price_text(market)
        d=round((mv-pv)/mv*100) if pv and mv and mv>pv else 0
        scorev=8+(4 if d>=40 else 3 if d>=25 else 1 if d>=10 else 0)
        st=sales.group(1) if sales else ""
        if re.search(r"[Kk]\+?\s*sold|ألف",st): scorev+=2
        out.append({
            "title":name[:150],
            "current_price":price,
            "old_price":market if mv and pv and mv>pv else "",
            "code":"alr408026",
            "url":"https://temu.to/k/e76r9skmde8",
            "source":"temu",
            "score":scorev,
            "discount":d,
            "verified_at":now,
            "source_url":f"https://www.temu.com/sa/product-g-{gid}.html",
            "tracking":"campaign_link_plus_code"
        })
        if len(out)>=7: break
    return out

def load_assistant_offers(now):
    path="laqta-control-center/assistant_offers.json"
    try:
        with open(path,"r",encoding="utf-8") as f:
            data=json.load(f)
        out=[]
        for x in data.get("offers",[]):
            if not x.get("title") or not x.get("url"): continue
            if is_blocked_offer(x.get("title",""),x.get("url","")): continue
            x=dict(x)
            x.setdefault("verified_at",now)
            x.setdefault("score",9)
            x.setdefault("discount",0)
            x.setdefault("tracking","assistant_verified")
            out.append(x)
        return out[:20]
    except Exception:
        return []

def build():
    now = datetime.now(timezone.utc).isoformat(timespec="seconds")
    slot = datetime.now(timezone.utc).hour // 4
    candidates = []
    candidates.extend(amazon_direct_deals(now))
    candidates.extend(load_assistant_offers(now))

    for store, cfg in STORES.items():
        # Automated price extraction is enabled only where we can verify the
        # exact destination and current deal page. Other stores are supplied
        # by assistant_offers after exact product-link verification.
        if store != "amazon":
            continue
        q = cfg["queries"][slot % len(cfg["queries"])]
        try:
            items = search(q)
        except Exception:
            items = []

        store_hits = []
        for it in items:
            u = it["url"]
            if store == "amazon" and "amazon.sa" not in u:
                continue
            if store == "noon" and "noon.com" not in u:
                continue
            if store == "temu" and "temu." not in u:
                continue
            if store == "shein" and "shein." not in u:
                continue
            direct_product = productish(store, u)
            sc, d, prices = score(store, it["title"], it["desc"], u)
            if not direct_product and prices and d >= 10:
                sc += 3
            if sc < 6:
                continue

            vals = sorted(set(prices))
            current = ""
            old = ""
            if vals:
                current = f"{vals[0]:g} ر.س"
                if len(vals) > 1 and vals[-1] > vals[0]:
                    old = f"{vals[-1]:g} ر.س"

            turl = tracked_url(store, u, cfg) if direct_product else cfg.get("fallback","")
            if store == "amazon" and not direct_product:
                continue
            if not turl:
                continue

            store_hits.append({
                "title": extract_product_title(it["desc"], it["title"]),
                "current_price": current,
                "old_price": old,
                "code": cfg["code"],
                "url": turl,
                "source": store,
                "score": sc,
                "discount": d,
                "verified_at": now,
                "source_url": u,
                "tracking": "amazon_tag" if store == "amazon" else ("product_url_plus_code" if direct_product else "campaign_link_plus_code"),
            })

        store_hits.sort(key=lambda z: z["score"], reverse=True)
        candidates.extend(store_hits[:5])

    seen = set()
    out = []
    for x in sorted(candidates, key=lambda z: z["score"], reverse=True):
        if is_blocked_offer(x.get("title",""),x.get("url","")): continue
        k = (x["source"], x["url"], x["title"].lower())
        if k in seen:
            continue
        seen.add(k)
        out.append(x)
        if len(out) >= 30:
            break

    return {"generated_at": now, "offers": out}

if __name__ == "__main__":
    data = build()
    with open("laqta-control-center/curated_offers.json", "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print("offers", len(data["offers"]))

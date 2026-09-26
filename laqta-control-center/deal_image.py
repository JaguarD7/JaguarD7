import hashlib, html, io, json, os, re
from pathlib import Path
from urllib.parse import urljoin

import httpx
from PIL import Image, ImageDraw, ImageFont, ImageOps
import arabic_reshaper
from bidi.algorithm import get_display

BASE = Path(__file__).resolve().parent
MEDIA_DIR = BASE / "generated"
MEDIA_DIR.mkdir(parents=True, exist_ok=True)
PUBLIC_BASE = os.getenv("LAQTA_PUBLIC_BASE", "https://laqta-control-center.onrender.com").rstrip("/")
DESIGN_VERSION = "v5-exact-product"

UA = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/154 Safari/537.36",
    "Accept-Language": "ar-SA,ar;q=0.9,en;q=0.8",
}

ARABIC_RE = re.compile(r"[\u0600-\u06ff]")

def _font(size, bold=False):
    candidates = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/dejavu/DejaVuSans.ttf",
    ]
    for p in candidates:
        if Path(p).exists():
            return ImageFont.truetype(p, size=size)
    return ImageFont.load_default()

def _display(s):
    s = str(s or "")
    if ARABIC_RE.search(s):
        try:
            return get_display(arabic_reshaper.reshape(s))
        except Exception:
            return s
    return s

def _money(v):
    m = re.search(r"\d+(?:\.\d+)?", str(v or "").replace(",", ""))
    return float(m.group()) if m else None

def _brand_store(source):
    s = str(source or "").lower()
    if "noon" in s: return "NOON"
    if "amazon" in s: return "AMAZON"
    if "temu" in s: return "TEMU"
    if "shein" in s: return "SHEIN"
    return "DEAL"

def _product_id(source, url):
    s=str(source or "").lower(); u=str(url or "")
    if "amazon" in s:
        m=re.search(r"/dp/([A-Z0-9]{10})",u,re.I); return m.group(1).upper() if m else ""
    if "noon" in s:
        m=re.search(r"/([A-Z0-9]{8,})/p/?",u,re.I); return m.group(1).upper() if m else ""
    if "shein" in s:
        m=re.search(r"-p-(\d+)\.html",u,re.I); return m.group(1) if m else ""
    if "temu" in s:
        m=re.search(r"(?:product-g-|g-)(\d+)",u,re.I); return m.group(1) if m else ""
    return ""

def _norm(s):
    s=html.unescape(re.sub(r"<[^>]+>"," ",str(s or ""))).lower()
    s=re.sub(r"[^\w\u0600-\u06ff]+"," ",s)
    return re.sub(r"\s+"," ",s).strip()

def _tokens(s):
    stop={"with","and","the","for","من","في","مع","على","الى","إلى","هذا","هذه","نسخة","version"}
    return {x for x in _norm(s).split() if len(x)>=3 and x not in stop}

def _similar(a,b):
    aa,bb=_tokens(a),_tokens(b)
    if not aa or not bb: return True
    overlap=len(aa & bb)
    return overlap >= 1 or overlap/max(1,min(len(aa),len(bb))) >= .28

def _walk_json(obj):
    if isinstance(obj,dict):
        yield obj
        for v in obj.values():
            yield from _walk_json(v)
    elif isinstance(obj,list):
        for v in obj:
            yield from _walk_json(v)

def _first_meta(page, attr, key):
    pats=[
        rf'<meta[^>]+{attr}=["\']{re.escape(key)}["\'][^>]+content=["\']([^"\']+)["\']',
        rf'<meta[^>]+content=["\']([^"\']+)["\'][^>]+{attr}=["\']{re.escape(key)}["\']'
    ]
    for pat in pats:
        m=re.search(pat,page,re.I|re.S)
        if m: return html.unescape(m.group(1)).strip()
    return ""

async def _product_meta(page_url, offer_title, source):
    async with httpx.AsyncClient(timeout=22,follow_redirects=True,headers=UA) as client:
        r=await client.get(page_url)
        r.raise_for_status()
        page=r.text; final_url=str(r.url)
        pid=_product_id(source, final_url) or _product_id(source, page_url)
        if pid and pid.lower() not in page.lower() and pid.lower() not in final_url.lower():
            raise ValueError("product_id_mismatch")

        product_name=""
        image_candidates=[]

        # Prefer schema.org Product data because it ties the image to the exact product.
        for m in re.finditer(r'<script[^>]+type=["\']application/ld\+json["\'][^>]*>(.*?)</script>',page,re.I|re.S):
            raw=html.unescape(m.group(1)).strip()
            try:
                obj=json.loads(raw)
            except Exception:
                continue
            for node in _walk_json(obj):
                typ=node.get("@type")
                types=typ if isinstance(typ,list) else [typ]
                if not any(str(x).lower()=="product" for x in types if x):
                    continue
                name=str(node.get("name") or "").strip()
                imgs=node.get("image")
                if isinstance(imgs,str): imgs=[imgs]
                elif isinstance(imgs,dict): imgs=[imgs.get("url") or imgs.get("contentUrl")]
                elif not isinstance(imgs,list): imgs=[]
                if name and _similar(name,offer_title):
                    product_name=name
                    for x in imgs:
                        if isinstance(x,str) and x: image_candidates.append(urljoin(final_url,html.unescape(x).replace("\\/","/")))
                    break
            if product_name: break

        og_title=_first_meta(page,"property","og:title") or _first_meta(page,"name","twitter:title")
        og_image=_first_meta(page,"property","og:image") or _first_meta(page,"name","twitter:image")
        page_title=""
        mt=re.search(r"<title[^>]*>(.*?)</title>",page,re.I|re.S)
        if mt: page_title=html.unescape(re.sub(r"<[^>]+>"," ",mt.group(1))).strip()

        exact_title=product_name or og_title or page_title or offer_title
        if not _similar(exact_title,offer_title):
            raise ValueError("title_mismatch")
        if og_image:
            image_candidates.append(urljoin(final_url,html.unescape(og_image).replace("\\/","/")))

        # Store-specific image fallbacks only after exact page/title validation.
        store_patterns=[
            r'"landingImage"\s*:\s*"([^"]+)"',
            r'"hiRes"\s*:\s*"([^"]+)"',
            r'"imageUrl"\s*:\s*"([^"]+)"',
            r'"image_url"\s*:\s*"([^"]+)"',
        ]
        for pat in store_patterns:
            for mm in re.finditer(pat,page,re.I|re.S):
                image_candidates.append(urljoin(final_url,html.unescape(mm.group(1)).replace("\\/","/")))
                if len(image_candidates)>=16: break

        seen=set()
        for u in image_candidates:
            if not u or u in seen: continue
            seen.add(u)
            low=u.lower()
            if any(x in low for x in ("logo","sprite","icon","banner","placeholder")): continue
            try:
                ir=await client.get(u)
                ir.raise_for_status()
                if "image" not in ir.headers.get("content-type",""): continue
                im=Image.open(io.BytesIO(ir.content)).convert("RGBA")
                if im.width<300 or im.height<300: continue
                # Reject extreme banner shapes; exact product images are normally near-square/portrait.
                ratio=max(im.width/im.height,im.height/im.width)
                if ratio>3.2: continue
                return {"title":exact_title[:170],"image":im,"final_url":final_url}
            except Exception:
                continue
    raise ValueError("no_verified_product_image")

def _bg(w,h,variant):
    palettes=[
        ((2,22,18),(9,55,45)),((8,16,29),(17,48,70)),((25,14,8),(69,41,20)),
        ((9,9,12),(43,38,30)),((6,28,23),(27,62,50)),((23,10,26),(61,27,56))
    ]
    a,b=palettes[variant%len(palettes)]
    img=Image.new("RGB",(w,h)); px=img.load()
    for y in range(h):
        for x in range(w):
            t=(x+y)/(w+h); px[x,y]=tuple(int(a[i]*(1-t)+b[i]*t) for i in range(3))
    return img.convert("RGBA")

def _measure(draw,text,font):
    return draw.textlength(_display(text),font=font)

def _wrap(draw,text,font,max_width,max_lines=3):
    text=re.sub(r"\s+"," ",str(text or "")).strip()
    words=text.split(); lines=[]; cur=""
    for word in words:
        test=(cur+" "+word).strip()
        if _measure(draw,test,font)<=max_width: cur=test
        else:
            if cur: lines.append(cur)
            cur=word
            if len(lines)>=max_lines-1: break
    if cur and len(lines)<max_lines: lines.append(cur)
    if len(" ".join(lines))<len(text) and lines:
        while _measure(draw,lines[-1]+"...",font)>max_width and len(lines[-1])>4: lines[-1]=lines[-1][:-1]
        lines[-1]=lines[-1].rstrip()+"..."
    return lines or [""]

def _text(draw,xy,text,font,fill,anchor=None):
    draw.text(xy,_display(text),font=font,fill=fill,anchor=anchor)

def _paste_product(canvas,product,box):
    x1,y1,x2,y2=box; size=(x2-x1,y2-y1)
    bg=Image.new("RGBA",size,(249,248,244,255))
    product=ImageOps.contain(product,(size[0]-32,size[1]-32),method=Image.Resampling.LANCZOS)
    bg.alpha_composite(product,((size[0]-product.width)//2,(size[1]-product.height)//2))
    mask=Image.new("L",size,0); d=ImageDraw.Draw(mask); d.rounded_rectangle((0,0,size[0]-1,size[1]-1),radius=30,fill=255)
    canvas.paste(bg,(x1,y1),mask)

def _brand(draw,W,gold,white,dark,variant,store):
    if variant%2:
        draw.rounded_rectangle((46,35,245,91),radius=28,fill=(255,255,255,15),outline=gold,width=1)
        _text(draw,(72,47),"LAQTA",_font(31,True),white)
    else:
        draw.ellipse((52,43,88,79),fill=gold); draw.polygon([(59,55),(69,48),(82,58),(70,71)],fill=dark)
        _text(draw,(104,39),"LAQTA",_font(34,True),white)
    bw=max(125,int(draw.textlength(store,font=_font(18,True)))+44)
    draw.rounded_rectangle((W-bw-48,42,W-48,86),radius=20,fill=(255,255,255,14),outline=gold,width=1)
    _text(draw,(W-bw-25,53),store,_font(18,True),gold)

async def prepare_deal_asset(offer):
    title=str(offer["title"] or "").strip()
    cp=str(offer["current_price"] or "").strip()
    old=str(offer["old_price"] or "").strip()
    code=str(offer["code"] or "").strip()
    source=str(offer["source"] or "").strip()
    url=str(offer["url"] or "").strip()

    meta=await _product_meta(url,title,source)
    exact_title=meta["title"]
    product=meta["image"]

    identity=f"{DESIGN_VERSION}|{source}|{url}|{exact_title}|{cp}|{old}|{code}"
    digest=hashlib.sha256(identity.encode("utf-8","ignore")).hexdigest()
    variant=int(digest[:2],16)%6
    name=digest[:24]+".png"; out=MEDIA_DIR/name
    if out.exists():
        return {"image_url":f"{PUBLIC_BASE}/media/{name}","title":exact_title}

    W,H=1200,675; canvas=_bg(W,H,variant); draw=ImageDraw.Draw(canvas)
    GOLD=(239,202,117,255); GOLD2=(255,225,157,255); WHITE=(248,248,244,255)
    MUTED=(185,192,188,255); DARK=(4,28,24,255); RED=(235,78,74,255)
    store=_brand_store(source); _brand(draw,W,GOLD,WHITE,DARK,variant,store)
    now=_money(cp); before=_money(old)
    pct=round((before-now)/before*100) if now is not None and before is not None and before>now else None

    # Six layouts; same LAQTA identity, different composition.
    if variant==0:
        _paste_product(canvas,product,(62,135,575,608)); tx,ty,mw=620,145,520
    elif variant==1:
        _paste_product(canvas,product,(690,125,1145,610)); tx,ty,mw=54,145,575
    elif variant==2:
        _paste_product(canvas,product,(395,135,805,430)); tx,ty,mw=600,450,1040
    elif variant==3:
        _text(draw,(54,120),"BIG DEAL",_font(58,True),GOLD)
        _paste_product(canvas,product,(60,215,545,600)); tx,ty,mw=590,140,555
    elif variant==4:
        _paste_product(canvas,product,(78,150,540,585)); tx,ty,mw=610,160,535
    else:
        _paste_product(canvas,product,(650,125,1135,530)); tx,ty,mw=54,150,545

    # Product title
    if variant==2:
        y=452
        for line in _wrap(draw,exact_title,_font(29,True),1040,2):
            _text(draw,(600,y),line,_font(29,True),WHITE,anchor="mm"); y+=39
        _text(draw,(600,555),cp or "CHECK PRICE",_font(48,True),GOLD2,anchor="mm")
        if pct is not None:
            draw.rounded_rectangle((76,150,240,195),radius=22,fill=GOLD)
            _text(draw,(158,173),f"-{pct}%",_font(22,True),DARK,anchor="mm")
    else:
        title_font=_font(34 if variant!=3 else 31,True)
        y=ty
        for line in _wrap(draw,exact_title,title_font,mw,3):
            rtl=bool(ARABIC_RE.search(line))
            x=tx+mw if rtl else tx
            _text(draw,(x,y),line,title_font,WHITE,anchor="ra" if rtl else None); y+=47
        py=max(y+20,345)
        if pct is not None:
            draw.rounded_rectangle((tx,py,tx+174,py+44),radius=22,fill=GOLD)
            _text(draw,(tx+87,py+22),f"SAVE {pct}%",_font(19,True),DARK,anchor="mm"); py+=58
        _text(draw,(tx,py),"NOW",_font(16,True),MUTED)
        _text(draw,(tx,py+23),cp or "CHECK PRICE",_font(50,True),GOLD2)
        if old:
            oy=py+85; _text(draw,(tx,oy),f"WAS {old}",_font(21,True),MUTED)
            ow=draw.textlength(f"WAS {old}",font=_font(21,True)); draw.line((tx,oy+14,tx+ow,oy+14),fill=RED,width=3)
        if code:
            cy=min(574,py+142)
            draw.rounded_rectangle((tx,cy,tx+370,cy+52),radius=15,outline=(239,202,117,155),width=1)
            _text(draw,(tx+18,cy+13),f"CODE  {code}",_font(24,True),WHITE)

    _text(draw,(52,642),"LAQTA • VERIFIED DEAL • AFFILIATE",_font(15,False),MUTED)
    canvas.convert("RGB").save(out,"PNG",optimize=True)
    return {"image_url":f"{PUBLIC_BASE}/media/{name}","title":exact_title}

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
        ((2,24,20),(10,63,51)),
        ((8,18,27),(18,54,65)),
        ((29,18,10),(78,51,26)),
        ((14,14,16),(52,44,30)),
        ((7,31,25),(36,74,57)),
    ]
    a,b=palettes[variant%len(palettes)]
    img=Image.new("RGB",(w,h))
    px=img.load()
    for y in range(h):
        for x in range(w):
            t=(0.7*x+0.3*y)/(0.7*w+0.3*h)
            px[x,y]=tuple(int(a[i]*(1-t)+b[i]*t) for i in range(3))
    return img.convert("RGBA")

def _measure(draw,text,font):
    return draw.textlength(_display(text),font=font)

def _wrap(draw,text,font,max_width,max_lines=2):
    text=re.sub(r"\s+"," ",str(text or "")).strip()
    words=text.split(); lines=[]; cur=""
    for word in words:
        test=(cur+" "+word).strip()
        if _measure(draw,test,font)<=max_width:
            cur=test
        else:
            if cur: lines.append(cur)
            cur=word
            if len(lines)>=max_lines-1: break
    if cur and len(lines)<max_lines: lines.append(cur)
    if len(" ".join(lines))<len(text) and lines:
        while _measure(draw,lines[-1]+"...",font)>max_width and len(lines[-1])>4:
            lines[-1]=lines[-1][:-1]
        lines[-1]=lines[-1].rstrip()+"..."
    return lines or [""]

def _text(draw,xy,text,font,fill,anchor=None):
    draw.text(xy,_display(text),font=font,fill=fill,anchor=anchor)

def _clean_title(title):
    t=re.sub(r"\s+"," ",str(title or "")).strip()
    t=re.sub(r"\s*[-|]\s*(?:Amazon|Noon|SHEIN|Temu|السعودية).*$","",t,flags=re.I)
    t=re.sub(r"\s+(?:with FaceTime|Middle East Version|نسخة الشرق الأوسط|مع FaceTime).*$","",t,flags=re.I)
    return t[:110].rstrip(" ,-")

def _card(canvas,box,fill=(250,248,241,255),radius=32,shadow=True):
    x1,y1,x2,y2=box
    if shadow:
        sh=Image.new("RGBA",canvas.size,(0,0,0,0))
        sd=ImageDraw.Draw(sh)
        sd.rounded_rectangle((x1+10,y1+14,x2+10,y2+14),radius=radius,fill=(0,0,0,75))
        sh=sh.filter(ImageFilter.GaussianBlur(14))
        canvas.alpha_composite(sh)
    layer=Image.new("RGBA",canvas.size,(0,0,0,0))
    d=ImageDraw.Draw(layer)
    d.rounded_rectangle(box,radius=radius,fill=fill)
    canvas.alpha_composite(layer)

def _paste_product(canvas,product,box,pad=24):
    x1,y1,x2,y2=box
    size=(x2-x1,y2-y1)
    stage=Image.new("RGBA",size,(250,248,242,255))
    product=ImageOps.contain(product,(size[0]-pad*2,size[1]-pad*2),method=Image.Resampling.LANCZOS)
    stage.alpha_composite(product,((size[0]-product.width)//2,(size[1]-product.height)//2))
    mask=Image.new("L",size,0)
    md=ImageDraw.Draw(mask); md.rounded_rectangle((0,0,size[0]-1,size[1]-1),radius=28,fill=255)
    canvas.paste(stage,(x1,y1),mask)

def _brand(draw,W,gold,white,variant,store):
    # compact, premium, always recognizable
    draw.rounded_rectangle((48,34,258,92),radius=29,fill=(255,255,255,16),outline=(239,202,117,130),width=1)
    _text(draw,(73,46),"LAQTA",_font(32,True),white)
    _text(draw,(190,55),"لقطة",_font(20,True),gold)
    bw=max(126,int(draw.textlength(store,font=_font(18,True)))+42)
    draw.rounded_rectangle((W-bw-48,40,W-48,88),radius=23,fill=(0,0,0,24),outline=(255,255,255,44),width=1)
    _text(draw,(W-bw/2-48,64),store,_font(18,True),gold,anchor="mm")

def _price_block(draw,x,y,cp,old,pct,gold,white,muted,red,large=True):
    if pct is not None:
        draw.rounded_rectangle((x,y,x+166,y+42),radius=21,fill=gold)
        _text(draw,(x+83,y+21),f"خصم {pct}%",_font(20,True),(8,31,25,255),anchor="mm")
        y+=58
    _text(draw,(x,y),"السعر الآن",_font(17,True),muted)
    _text(draw,(x,y+27),cp or "تحقق من السعر",_font(52 if large else 44,True),white)
    if old:
        oy=y+(91 if large else 80)
        _text(draw,(x,oy),f"بدل {old}",_font(22,True),muted)
        ww=_measure(draw,f"بدل {old}",_font(22,True))
        draw.line((x,oy+15,x+ww,oy+15),fill=red,width=3)
    return y

def _code_badge(draw,x,y,code,gold,white):
    if not code: return
    draw.rounded_rectangle((x,y,x+345,y+54),radius=16,fill=(255,255,255,14),outline=(239,202,117,150),width=1)
    _text(draw,(x+18,y+12),"الكود",_font(17,True),gold)
    _text(draw,(x+111,y+10),code,_font(28,True),white)

async def prepare_deal_asset(offer):
    title=str(offer["title"] or "").strip()
    cp=str(offer["current_price"] or "").strip()
    old=str(offer["old_price"] or "").strip()
    code=str(offer["code"] or "").strip()
    source=str(offer["source"] or "").strip()
    url=str(offer["url"] or "").strip()

    meta=await _product_meta(url,title,source)
    exact_title=_clean_title(meta["title"])
    product=meta["image"]

    identity=f"v6-luxury|{source}|{url}|{exact_title}|{cp}|{old}|{code}"
    digest=hashlib.sha256(identity.encode("utf-8","ignore")).hexdigest()
    variant=int(digest[:2],16)%5
    name=digest[:24]+".png"
    out=MEDIA_DIR/name
    if out.exists():
        return {"image_url":f"{PUBLIC_BASE}/media/{name}","title":exact_title}

    W,H=1200,675
    canvas=_bg(W,H,variant)
    draw=ImageDraw.Draw(canvas)

    GOLD=(239,202,117,255)
    GOLD2=(255,228,169,255)
    WHITE=(249,249,245,255)
    MUTED=(190,198,193,255)
    RED=(235,88,78,255)
    store=_brand_store(source)
    _brand(draw,W,GOLD,WHITE,variant,store)

    now=_money(cp); before=_money(old)
    pct=round((before-now)/before*100) if now is not None and before is not None and before>now else None

    # subtle decorative orbs to make the card feel designed, not templated
    for ox,oy,rr,alpha in ((1040,560,170,24),(1050,130,95,20),(120,600,110,14)):
        draw.ellipse((ox-rr,oy-rr,ox+rr,oy+rr),fill=(239,202,117,alpha))

    if variant==0:
        # product right, copy left
        _card(canvas,(665,124,1148,614))
        _paste_product(canvas,product,(686,145,1127,593))
        _text(draw,(55,137),"لقطة اليوم",_font(23,True),GOLD)
        y=181
        for line in _wrap(draw,exact_title,_font(38,True),555,2):
            _text(draw,(610,y),line,_font(38,True),WHITE,anchor="ra"); y+=52
        py=max(315,y+20)
        _price_block(draw,55,py,cp,old,pct,GOLD,GOLD2,MUTED,RED)
        _code_badge(draw,55,545,code,GOLD,WHITE)

    elif variant==1:
        # product left, elegant editorial copy right
        _card(canvas,(54,124,550,612))
        _paste_product(canvas,product,(75,145,529,591))
        _text(draw,(610,145),"اختيار لقطة",_font(22,True),GOLD)
        y=188
        for line in _wrap(draw,exact_title,_font(36,True),530,2):
            _text(draw,(1140,y),line,_font(36,True),WHITE,anchor="ra"); y+=50
        _price_block(draw,610,max(330,y+28),cp,old,pct,GOLD,GOLD2,MUTED,RED)
        _code_badge(draw,610,548,code,GOLD,WHITE)

    elif variant==2:
        # centered hero product with floating discount
        _card(canvas,(296,122,904,442))
        _paste_product(canvas,product,(318,143,882,420))
        if pct is not None:
            draw.ellipse((842,112,1010,280),fill=GOLD)
            _text(draw,(926,184),f"-{pct}%",_font(34,True),(7,30,24,255),anchor="mm")
            _text(draw,(926,218),"خصم",_font(18,True),(7,30,24,255),anchor="mm")
        y=477
        for line in _wrap(draw,exact_title,_font(30,True),1030,2):
            _text(draw,(600,y),line,_font(30,True),WHITE,anchor="mm"); y+=39
        _text(draw,(600,578),cp or "تحقق من السعر",_font(48,True),GOLD2,anchor="mm")
        if code:
            _text(draw,(600,625),f"كود {code}",_font(20,True),WHITE,anchor="mm")

    elif variant==3:
        # bold price-first card
        _text(draw,(55,130),"سعر يلفت 👀",_font(29,True),GOLD)
        _text(draw,(55,182),cp or "تحقق من السعر",_font(68,True),GOLD2)
        if pct is not None:
            draw.rounded_rectangle((55,268,230,314),radius=23,fill=GOLD)
            _text(draw,(142,291),f"خصم {pct}%",_font(21,True),(7,30,24,255),anchor="mm")
        y=345
        for line in _wrap(draw,exact_title,_font(33,True),520,2):
            _text(draw,(575,y),line,_font(33,True),WHITE,anchor="ra"); y+=46
        if old:
            _text(draw,(55,470),f"كان {old}",_font(22,True),MUTED)
        _code_badge(draw,55,540,code,GOLD,WHITE)
        _card(canvas,(665,124,1148,614))
        _paste_product(canvas,product,(686,145,1127,593))

    else:
        # magazine split with cream stripe
        draw.rounded_rectangle((42,120,1158,620),radius=38,fill=(255,255,255,12),outline=(239,202,117,80),width=2)
        _paste_product(canvas,product,(62,145,545,592))
        _text(draw,(610,150),"عرض مختار",_font(22,True),GOLD)
        y=195
        for line in _wrap(draw,exact_title,_font(35,True),500,2):
            _text(draw,(1110,y),line,_font(35,True),WHITE,anchor="ra"); y+=49
        _price_block(draw,610,max(330,y+18),cp,old,pct,GOLD,GOLD2,MUTED,RED)
        _code_badge(draw,610,548,code,GOLD,WHITE)

    # tiny footer only; no clutter
    draw.line((48,636,1152,636),fill=(255,255,255,24),width=1)
    _text(draw,(55,646),"LAQTA • عروض مختارة وموثقة",_font(14,False),MUTED)
    canvas.convert("RGB").save(out,"PNG",optimize=True)
    return {"image_url":f"{PUBLIC_BASE}/media/{name}","title":exact_title}

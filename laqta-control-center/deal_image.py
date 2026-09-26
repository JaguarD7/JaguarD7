import hashlib, html, io, os, re
from pathlib import Path
from urllib.parse import urljoin

import httpx
from PIL import Image, ImageDraw, ImageFont, ImageOps, ImageFilter

BASE = Path(__file__).resolve().parent
MEDIA_DIR = BASE / "generated"
MEDIA_DIR.mkdir(parents=True, exist_ok=True)
PUBLIC_BASE = os.getenv("LAQTA_PUBLIC_BASE", "https://laqta-control-center.onrender.com").rstrip("/")
DESIGN_VERSION = "v3"

UA = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/154 Safari/537.36",
    "Accept-Language": "ar-SA,ar;q=0.9,en;q=0.8",
}

def _font(size, bold=False):
    candidates = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/dejavu/DejaVuSans.ttf",
    ]
    for p in candidates:
        if Path(p).exists():
            return ImageFont.truetype(p, size=size)
    return ImageFont.load_default()

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

def _wrap(draw, text, font, max_width, max_lines=3):
    text = re.sub(r"\s+", " ", str(text or "")).strip()
    words, lines, cur = text.split(), [], ""
    for word in words:
        test = (cur + " " + word).strip()
        if draw.textlength(test, font=font) <= max_width:
            cur = test
        else:
            if cur: lines.append(cur)
            cur = word
            if len(lines) >= max_lines - 1: break
    if cur and len(lines) < max_lines: lines.append(cur)
    if len(" ".join(lines)) < len(text) and lines:
        while draw.textlength(lines[-1] + "...", font=font) > max_width and len(lines[-1]) > 4:
            lines[-1] = lines[-1][:-1]
        lines[-1] = lines[-1].rstrip() + "..."
    return lines[:max_lines] or [""]

async def _fetch_product_image(page_url):
    try:
        async with httpx.AsyncClient(timeout=20, follow_redirects=True, headers=UA) as client:
            r = await client.get(page_url)
            r.raise_for_status()
            page, final_url = r.text, str(r.url)
            patterns = [
                r'<meta[^>]+property=["\']og:image["\'][^>]+content=["\']([^"\']+)["\']',
                r'<meta[^>]+content=["\']([^"\']+)["\'][^>]+property=["\']og:image["\']',
                r'<meta[^>]+name=["\']twitter:image(?::src)?["\'][^>]+content=["\']([^"\']+)["\']',
                r'"imageUrl"\s*:\s*"([^"]+)"',
                r'"image_url"\s*:\s*"([^"]+)"',
                r'"image"\s*:\s*"([^"]+)"',
                r'"src"\s*:\s*"([^"]+\.(?:jpg|jpeg|png|webp)[^"]*)"',
            ]
            candidates = []
            for pat in patterns:
                for m in re.finditer(pat, page, re.I | re.S):
                    u = html.unescape(m.group(1)).replace("\\/", "/")
                    u = urljoin(final_url, u)
                    if u not in candidates:
                        candidates.append(u)
                    if len(candidates) >= 12: break
            for u in candidates:
                try:
                    ir = await client.get(u)
                    ir.raise_for_status()
                    if "image" not in ir.headers.get("content-type", ""): continue
                    im = Image.open(io.BytesIO(ir.content)).convert("RGBA")
                    if im.width < 250 or im.height < 250: continue
                    return im
                except Exception:
                    continue
    except Exception:
        pass
    return None

def _bg(w,h,variant):
    palettes = [
        ((2,22,18),(9,55,45)), ((5,18,32),(10,43,65)),
        ((26,15,10),(73,41,20)), ((10,10,14),(45,39,29)),
        ((7,29,24),(27,65,52)), ((22,10,25),(65,28,58))
    ]
    a,b = palettes[variant % len(palettes)]
    img = Image.new("RGB",(w,h)); px=img.load()
    for y in range(h):
        for x in range(w):
            t=(x+y)/(w+h)
            px[x,y]=tuple(int(a[i]*(1-t)+b[i]*t) for i in range(3))
    return img.convert("RGBA")

def _brand_header(draw, W, gold, white, dark, variant, store):
    if variant % 3 == 0:
        draw.ellipse((52,42,88,78),fill=gold); draw.polygon([(59,54),(69,47),(82,57),(70,70)],fill=dark)
        draw.text((104,38),"LAQTA",font=_font(34,True),fill=white)
    elif variant % 3 == 1:
        draw.rounded_rectangle((48,38,252,91),radius=26,fill=(255,255,255,18),outline=gold,width=1)
        draw.text((75,48),"LAQTA",font=_font(31,True),fill=white)
    else:
        draw.text((50,40),"LAQTA / لقطة",font=_font(31,True),fill=white)
        draw.line((50,88,355,88),fill=gold,width=3)
    bw=max(118,int(draw.textlength(store,font=_font(18,True)))+42)
    draw.rounded_rectangle((W-bw-48,42,W-48,86),radius=20,fill=(255,255,255,16),outline=gold,width=1)
    draw.text((W-bw-28,53),store,font=_font(18,True),fill=gold)

def _paste_product(canvas, product, box, rounded=True):
    x1,y1,x2,y2=box
    bg=Image.new("RGBA",(x2-x1,y2-y1),(248,247,242,255))
    if rounded:
        mask=Image.new("L",bg.size,0); d=ImageDraw.Draw(mask); d.rounded_rectangle((0,0,bg.width-1,bg.height-1),radius=28,fill=255)
    else:
        mask=None
    if product is not None:
        contained=ImageOps.contain(product,(bg.width-30,bg.height-30),method=Image.Resampling.LANCZOS)
        px=(bg.width-contained.width)//2; py=(bg.height-contained.height)//2
        bg.alpha_composite(contained,(px,py))
    if mask:
        canvas.paste(bg,(x1,y1),mask)
    else:
        canvas.alpha_composite(bg,(x1,y1))

def _draw_price(draw, x, y, cp, old, pct, gold, white, muted, red, compact=False):
    if pct is not None:
        draw.rounded_rectangle((x,y,x+170,y+44),radius=22,fill=gold)
        draw.text((x+22,y+10),f"SAVE {pct}%",font=_font(19,True),fill=(4,28,24,255))
        y+=58
    draw.text((x,y),"NOW",font=_font(16,True),fill=muted)
    draw.text((x,y+23),cp or "CHECK PRICE",font=_font(44 if compact else 54,True),fill=white)
    if old:
        yy=y+(78 if compact else 88)
        draw.text((x,yy),f"WAS {old}",font=_font(21,True),fill=muted)
        w=draw.textlength(f"WAS {old}",font=_font(21,True))
        draw.line((x,yy+14,x+w,yy+14),fill=red,width=3)

async def make_deal_image(offer):
    title=str(offer["title"] or "").strip()
    cp=str(offer["current_price"] or "").strip()
    old=str(offer["old_price"] or "").strip()
    code=str(offer["code"] or "").strip()
    source=str(offer["source"] or "").strip()
    url=str(offer["url"] or "").strip()

    identity=f"{DESIGN_VERSION}|{source}|{url}|{title}|{cp}|{old}|{code}"
    digest=hashlib.sha256(identity.encode("utf-8","ignore")).hexdigest()
    variant=int(digest[:2],16)%6
    name=digest[:24]+".png"
    out=MEDIA_DIR/name
    if out.exists(): return f"{PUBLIC_BASE}/media/{name}"

    W,H=1200,675
    canvas=_bg(W,H,variant); draw=ImageDraw.Draw(canvas)
    GOLD=(239,202,117,255); GOLD2=(255,226,164,255); WHITE=(248,248,244,255)
    MUTED=(190,196,192,255); DARK=(4,28,24,255); RED=(235,78,74,255)
    store=_brand_store(source)
    _brand_header(draw,W,GOLD,WHITE,DARK,variant,store)
    product=await _fetch_product_image(url)

    now=_money(cp); before=_money(old)
    pct=round((before-now)/before*100) if now is not None and before is not None and before>now else None
    title_font=_font(32 if variant in (2,4) else 35,True)

    if variant==0:
        # classic split
        draw.rounded_rectangle((42,116,1158,630),radius=36,outline=(239,202,117,90),width=2)
        _paste_product(canvas,product,(62,144,574,606))
        y=148
        for line in _wrap(draw,title,title_font,520,3):
            draw.text((624,y),line,font=title_font,fill=WHITE); y+=47
        _draw_price(draw,624,336,cp,old,pct,GOLD,GOLD2,MUTED,RED)
        if code: draw.text((624,553),f"CODE  {code}",font=_font(27,True),fill=WHITE)

    elif variant==1:
        # product right, copy left
        _paste_product(canvas,product,(690,128,1145,615))
        draw.text((55,128),"TODAY'S PICK",font=_font(18,True),fill=GOLD)
        y=166
        for line in _wrap(draw,title,_font(38,True),570,3):
            draw.text((55,y),line,font=_font(38,True),fill=WHITE); y+=52
        _draw_price(draw,55,360,cp,old,pct,GOLD,GOLD2,MUTED,RED)
        if code:
            draw.rounded_rectangle((55,555,430,612),radius=16,outline=GOLD,width=1)
            draw.text((78,568),f"CODE  {code}",font=_font(25,True),fill=WHITE)

    elif variant==2:
        # centered magazine card
        draw.rounded_rectangle((55,118,1145,616),radius=42,fill=(255,255,255,12),outline=(239,202,117,80),width=2)
        _paste_product(canvas,product,(395,145,805,438))
        y=456
        lines=_wrap(draw,title,_font(30,True),1040,2)
        for line in lines:
            w=draw.textlength(line,font=_font(30,True)); draw.text(((W-w)/2,y),line,font=_font(30,True),fill=WHITE); y+=40
        ptxt=cp or "CHECK PRICE"
        w=draw.textlength(ptxt,font=_font(46,True)); draw.text(((W-w)/2,548),ptxt,font=_font(46,True),fill=GOLD2)
        if pct is not None:
            draw.rounded_rectangle((75,150,240,195),radius=22,fill=GOLD)
            draw.text((96,160),f"-{pct}%",font=_font(22,True),fill=DARK)

    elif variant==3:
        # bold offer poster
        draw.text((55,125),"BIG DEAL",font=_font(66,True),fill=GOLD)
        _paste_product(canvas,product,(60,225,545,604))
        y=142
        for line in _wrap(draw,title,_font(31,True),560,3):
            draw.text((590,y),line,font=_font(31,True),fill=WHITE); y+=44
        _draw_price(draw,590,345,cp,old,pct,GOLD,GOLD2,MUTED,RED)
        if code: draw.text((590,553),f"USE {code}",font=_font(28,True),fill=WHITE)

    elif variant==4:
        # minimal luxury
        draw.line((55,118,1145,118),fill=(239,202,117,95),width=2)
        _paste_product(canvas,product,(80,160,540,575))
        y=165
        for line in _wrap(draw,title,_font(34,True),540,3):
            draw.text((610,y),line,font=_font(34,True),fill=WHITE); y+=49
        draw.text((610,345),cp or "CHECK PRICE",font=_font(62,True),fill=GOLD2)
        if old:
            draw.text((614,426),old,font=_font(23,True),fill=MUTED)
            ow=draw.textlength(old,font=_font(23,True)); draw.line((612,441,612+ow,441),fill=RED,width=3)
        if pct is not None: draw.text((610,475),f"{pct}% OFF",font=_font(32,True),fill=GOLD)
        if code: draw.text((610,540),f"CODE: {code}",font=_font(25,True),fill=WHITE)

    else:
        # diagonal editorial
        draw.polygon([(0,520),(0,675),(1200,675),(1200,420)],fill=(0,0,0,52))
        _paste_product(canvas,product,(650,130,1135,535))
        draw.text((55,145),"LAQTA FIND",font=_font(19,True),fill=GOLD)
        y=184
        for line in _wrap(draw,title,_font(37,True),540,3):
            draw.text((55,y),line,font=_font(37,True),fill=WHITE); y+=50
        _draw_price(draw,55,390,cp,old,pct,GOLD,GOLD2,MUTED,RED,compact=True)
        if code: draw.text((760,563),f"CODE  {code}",font=_font(25,True),fill=WHITE)

    draw.text((52,642),"LAQTA • Verified deal • Price may change • Affiliate link",font=_font(15,False),fill=(183,190,186,255))
    canvas.convert("RGB").save(out,"PNG",optimize=True)
    return f"{PUBLIC_BASE}/media/{name}"

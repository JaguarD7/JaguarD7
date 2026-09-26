import hashlib, html, io, os, re
from pathlib import Path
from urllib.parse import urljoin

import httpx
from PIL import Image, ImageDraw, ImageFont, ImageOps

BASE = Path(__file__).resolve().parent
MEDIA_DIR = BASE / "generated"
MEDIA_DIR.mkdir(parents=True, exist_ok=True)
PUBLIC_BASE = os.getenv("LAQTA_PUBLIC_BASE", "https://laqta-control-center.onrender.com").rstrip("/")

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
    s = str(v or "").replace(",", "")
    m = re.search(r"\d+(?:\.\d+)?", s)
    return float(m.group()) if m else None

def _brand_store(source):
    s = str(source or "").lower()
    if "noon" in s:
        return "NOON"
    if "amazon" in s:
        return "AMAZON"
    if "temu" in s:
        return "TEMU"
    if "shein" in s:
        return "SHEIN"
    return "DEAL"

def _wrap(draw, text, font, max_width, max_lines=3):
    text = re.sub(r"\s+", " ", str(text or "")).strip()
    if not text:
        return [""]
    words = text.split()
    lines, cur = [], ""
    for word in words:
        test = (cur + " " + word).strip()
        if draw.textlength(test, font=font) <= max_width:
            cur = test
        else:
            if cur:
                lines.append(cur)
            cur = word
            if len(lines) >= max_lines - 1:
                break
    if cur and len(lines) < max_lines:
        lines.append(cur)
    consumed = " ".join(lines)
    if len(consumed) < len(text) and lines:
        while draw.textlength(lines[-1] + "...", font=font) > max_width and len(lines[-1]) > 4:
            lines[-1] = lines[-1][:-1]
        lines[-1] = lines[-1].rstrip() + "..."
    return lines[:max_lines]

async def _fetch_product_image(page_url):
    try:
        async with httpx.AsyncClient(timeout=18, follow_redirects=True, headers=UA) as client:
            r = await client.get(page_url)
            r.raise_for_status()
            page = r.text
            final_url = str(r.url)
            patterns = [
                r'<meta[^>]+property=["\']og:image["\'][^>]+content=["\']([^"\']+)["\']',
                r'<meta[^>]+content=["\']([^"\']+)["\'][^>]+property=["\']og:image["\']',
                r'<meta[^>]+name=["\']twitter:image(?::src)?["\'][^>]+content=["\']([^"\']+)["\']',
                r'<meta[^>]+content=["\']([^"\']+)["\'][^>]+name=["\']twitter:image(?::src)?["\']',
                r'"image"\s*:\s*"([^"]+)"',
            ]
            img_url = ""
            for pat in patterns:
                m = re.search(pat, page, re.I | re.S)
                if m:
                    img_url = html.unescape(m.group(1)).replace("\\/", "/")
                    break
            if not img_url:
                return None
            img_url = urljoin(final_url, img_url)
            ir = await client.get(img_url)
            ir.raise_for_status()
            if "image" not in ir.headers.get("content-type", ""):
                return None
            im = Image.open(io.BytesIO(ir.content)).convert("RGBA")
            return im
    except Exception:
        return None

def _gradient_bg(w, h):
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(w):
            t = x / max(1, w - 1)
            v = y / max(1, h - 1)
            r = int(2 + 7*t + 5*v)
            g = int(27 + 21*t + 9*v)
            b = int(23 + 10*t + 6*v)
            px[x, y] = (r, g, b)
    return img.convert("RGBA")

def _rounded_mask(size, radius):
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((0, 0, size[0]-1, size[1]-1), radius=radius, fill=255)
    return m

async def make_deal_image(offer):
    title = str(offer["title"] or "").strip()
    cp = str(offer["current_price"] or "").strip()
    old = str(offer["old_price"] or "").strip()
    code = str(offer["code"] or "").strip()
    source = str(offer["source"] or "").strip()
    url = str(offer["url"] or "").strip()

    identity = f"{source}|{url}|{title}|{cp}|{old}|{code}"
    name = hashlib.sha256(identity.encode("utf-8", "ignore")).hexdigest()[:24] + ".png"
    out = MEDIA_DIR / name
    if out.exists():
        return f"{PUBLIC_BASE}/media/{name}"

    W, H = 1200, 675
    canvas = _gradient_bg(W, H)
    draw = ImageDraw.Draw(canvas)

    GOLD = (239, 202, 117, 255)
    GOLD2 = (255, 224, 153, 255)
    WHITE = (247, 247, 242, 255)
    MUTED = (181, 190, 184, 255)
    DARK = (4, 28, 24, 255)
    RED = (235, 78, 74, 255)

    # fixed brand frame
    draw.rounded_rectangle((34, 30, W-34, H-30), radius=32, outline=(239,202,117,105), width=2)
    draw.ellipse((72, 54, 106, 88), fill=GOLD)
    draw.polygon([(78,65),(88,58),(101,67),(90,80)], fill=DARK)
    draw.text((122, 47), "LAQTA", font=_font(35, True), fill=WHITE)
    draw.text((122, 87), "DEAL DROP", font=_font(16, True), fill=GOLD)
    draw.line((65, 128, W-65, 128), fill=(239,202,117,80), width=2)

    # product stage
    stage = (64, 160, 585, 612)
    draw.rounded_rectangle(stage, radius=34, fill=(248,247,241,255), outline=(255,255,255,45), width=1)

    product = await _fetch_product_image(url)
    if product is not None:
        box_w, box_h = 470, 400
        contained = ImageOps.contain(product, (box_w, box_h), method=Image.Resampling.LANCZOS)
        # white background removal is intentionally avoided; keep original product authenticity.
        x = stage[0] + (stage[2]-stage[0]-contained.width)//2
        y = stage[1] + (stage[3]-stage[1]-contained.height)//2 + 10
        if contained.mode == "RGBA":
            canvas.alpha_composite(contained, (x, y))
        else:
            canvas.paste(contained, (x, y))
    else:
        # branded fallback if store blocks image scraping
        cx = (stage[0] + stage[2]) // 2
        cy = (stage[1] + stage[3]) // 2
        draw.ellipse((cx-100, cy-100, cx+100, cy+100), fill=(8,46,38,255))
        draw.text((cx-74, cy-27), _brand_store(source), font=_font(29, True), fill=GOLD)

    # store badge
    store = _brand_store(source)
    badge_w = max(130, int(draw.textlength(store, font=_font(19, True))) + 48)
    draw.rounded_rectangle((W-badge_w-66, 50, W-66, 94), radius=22, fill=(239,202,117,34), outline=GOLD, width=1)
    draw.text((W-badge_w-42, 61), store, font=_font(19, True), fill=GOLD2)

    # product title
    title_font = _font(34, True)
    x0, y0 = 635, 164
    lines = _wrap(draw, title, title_font, 500, 3)
    for line in lines:
        draw.text((x0, y0), line, font=title_font, fill=WHITE)
        y0 += 48

    now = _money(cp)
    before = _money(old)
    pct = round((before-now)/before*100) if now is not None and before is not None and before > now else None

    # discount badge
    if pct is not None:
        label = f"SAVE {pct}%"
        draw.rounded_rectangle((635, 323, 825, 372), radius=24, fill=GOLD)
        draw.text((661, 334), label, font=_font(21, True), fill=DARK)

    # price block
    draw.text((635, 398), "NOW", font=_font(18, True), fill=MUTED)
    price_text = cp or "CHECK PRICE"
    draw.text((635, 421), price_text, font=_font(54, True), fill=GOLD2)

    if old:
        old_y = 492
        draw.text((638, old_y), f"WAS  {old}", font=_font(23, True), fill=MUTED)
        w = draw.textlength(f"WAS  {old}", font=_font(23, True))
        draw.line((635, old_y+15, 635+w, old_y+15), fill=RED, width=4)

    if code:
        draw.rounded_rectangle((635, 540, 1015, 598), radius=17, fill=(255,255,255,14), outline=(239,202,117,120), width=1)
        draw.text((657, 553), "CODE", font=_font(17, True), fill=MUTED)
        draw.text((750, 547), code, font=_font(30, True), fill=WHITE)

    # visual trust/footer
    draw.text((64, 630), "Verified deal • Price may change • Affiliate link", font=_font(16, False), fill=(178,187,181,255))
    draw.text((1060, 625), "LAQTA", font=_font(20, True), fill=GOLD)

    canvas.convert("RGB").save(out, "PNG", optimize=True)
    return f"{PUBLIC_BASE}/media/{name}"

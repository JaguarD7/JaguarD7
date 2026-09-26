import os
import uuid
import threading
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

BASE = Path(__file__).resolve().parent
STATIC = BASE / "static"
UPLOADS = BASE / "uploads"
UPLOADS.mkdir(exist_ok=True)

app = FastAPI(title="ONYX Motion Studio", version="1.2.0")
app.mount("/static", StaticFiles(directory=STATIC), name="static")
app.mount("/outputs", StaticFiles(directory=UPLOADS), name="outputs")

JOBS: dict[str, dict] = {}
FREE_ENGINES = [
    {"name": "Wan 2.2 14B Fast", "space": "zerogpu-aoti/wan2-2-fp8da-aoti-faster", "api": "/generate_video"},
    {"name": "Wan 2.2 14B Preview", "space": "r3gm/Wan2.2-14B-Preview", "api": "/generate_video"},
    {"name": "Wan 2.2 14B Fast Preview", "space": "kulkas2pintu/Wan2.2-14B-Fast-Preview", "api": "/generate_video"},
]
ENGINE_COOLDOWNS: dict[str, float] = {}

@app.get("/")
def index():
    return FileResponse(STATIC / "index.html")

@app.get("/health")
def health():
    return {
        "ok": True,
        "service": "onyx-motion-studio",
        "provider_configured": True,
        "engine": "ONYX Free Engine Router",
        "engine_mode": "multi_engine_free_router",
    }

@app.get("/api/capabilities")
def capabilities():
    return {
        "image_to_video": {
            "available": True,
            "engine": "Wan 2.2 14B",
            "free_prompt": True,
            "max_single_clip_seconds": 5,
            "queue_possible": True,
        },
        "video_face_swap": {"available": False, "status": "coming_next"},
        "talking_video": {"available": False, "status": "coming_next"},
        "provider_configured": True,
    }

def normalize_arabic_prompt(raw: str) -> str:
    # Lightweight local Arabic director: keeps the user's text and adds explicit English intent
    # for common motion/edit instructions so Wan receives English semantic anchors.
    low = raw.lower()
    intents = []
    wardrobe = {
        "بكيني": "wear a bikini",
        "فستان": "wear a dress",
        "بدلة": "wear a suit",
        "ملابس": "change the requested clothing/outfit",
        "لبسني": "change my outfit as requested",
    }
    actions = {
        "ارقص": "dance with clear full-body rhythmic movement",
        "رقصني": "dance with clear full-body rhythmic movement",
        "رقص": "dance with clear full-body rhythmic movement",
        "امشي": "walk naturally",
        "اجري": "run naturally",
        "اركض": "run naturally",
        "ابتسم": "smile naturally",
        "لف": "turn naturally",
    }
    for ar, en in {**wardrobe, **actions}.items():
        if ar in low and en not in intents:
            intents.append(en)
    if intents:
        return raw + ". Interpreted intent in English: " + ", ".join(intents)
    return raw

def direct_prompt(prompt: str) -> str:
    raw = (prompt or "").strip()
    raw = normalize_arabic_prompt(raw)
    if not raw:
        return "cinematic natural motion, realistic body movement, subtle expression, smooth camera motion, preserve subject identity"
    low = raw.lower()
    parts = [raw]
    if any(w in low for w in ["wear", "dress", "outfit", "bikini", "clothes", "clothing"]):
        parts.append("clearly apply the requested wardrobe change to the subject from the first frame, coherent fabric and anatomy")
    if any(w in low for w in ["dance", "dancing", "رقص", "ارقص", "رقصني"]):
        parts.append("full-body rhythmic dancing, coordinated arms, hips and footwork, natural weight shifts, continuous energetic motion")
    if any(w in low for w in ["walk", "walking", "run", "running"]):
        parts.append("clear full-body locomotion with realistic steps and natural limb coordination")
    parts += [
        "preserve the same person's facial identity and recognizable features",
        "realistic anatomy, consistent clothing and body across frames",
        "cinematic lighting, detailed skin, stable temporal consistency",
        "smooth coherent motion, no frozen pose, no abrupt morphing"
    ]
    return ", ".join(parts)

def run_wan(job_id: str, source_path: Path, prompt: str, duration: int):
    job = JOBS[job_id]
    enhanced_prompt = direct_prompt(prompt)
    job["directed_prompt"] = enhanced_prompt
    try:
        job["status"] = "connecting"
        job["progress"] = 8
        from gradio_client import Client, handle_file
        import time

        result = None
        errors = []
        quota_blocked = False
        for idx, engine in enumerate(FREE_ENGINES):
            if ENGINE_COOLDOWNS.get(engine["space"], 0) > time.time():
                continue
            job["status"] = "queued"
            job["progress"] = 15 + idx * 8
            job["engine"] = engine["name"]
            try:
                client = Client(engine["space"], verbose=False)
                result = client.predict(
                    input_image=handle_file(str(source_path)),
                    prompt=enhanced_prompt,
                    steps=6,
                    negative_prompt="low quality, blurry, distorted anatomy, extra fingers, deformed face, static frame, subtitles, watermark",
                    duration_seconds=float(max(0.5, min(duration, 5))),
                    guidance_scale=1.0,
                    guidance_scale_2=1.0,
                    seed=42,
                    randomize_seed=True,
                    api_name=engine["api"],
                )
                break
            except Exception as engine_exc:
                msg = str(engine_exc)
                errors.append(engine["name"] + ": " + msg[:180])
                lowerr = msg.lower()
                if "quota" in lowerr or "exceeded" in lowerr:
                    quota_blocked = True
                    ENGINE_COOLDOWNS[engine["space"]] = time.time() + 3600
                    # ZeroGPU quota is often account/IP scoped, so don't hammer every mirror.
                    break
                ENGINE_COOLDOWNS[engine["space"]] = time.time() + 300
                continue
        if result is None:
            if quota_blocked:
                job["status"] = "waiting_capacity"
                job["progress"] = 0
                job["error"] = "Free GPU quota is temporarily exhausted. ONYX will need another independent free provider or the quota reset."
                return
            raise RuntimeError("All free engines are temporarily unavailable. " + " | ".join(errors[-2:]))
        job["progress"] = 92
        video_obj = result[0] if isinstance(result, (list, tuple)) else result
        remote_path = getattr(video_obj, "path", None) or (video_obj.get("path") if isinstance(video_obj, dict) else None) or str(video_obj)
        src = Path(remote_path)
        if not src.exists():
            raise RuntimeError("The free engine finished but did not return a downloadable video file.")
        dest = UPLOADS / f"{job_id}_result.mp4"
        dest.write_bytes(src.read_bytes())
        job["output_url"] = f"/outputs/{dest.name}"
        job["status"] = "completed"
        job["progress"] = 100
    except Exception as exc:
        job["status"] = "failed"
        job["progress"] = 0
        job["error"] = str(exc)[:600]

@app.post("/api/jobs")
async def create_job(
    mode: Literal["image-to-video", "face-swap", "talking-video"] = Form(...),
    prompt: str = Form(""),
    duration: int = Form(5),
    identity_strength: int = Form(90),
    source: UploadFile | None = File(None),
    reference: UploadFile | None = File(None),
):
    job_id = uuid.uuid4().hex[:12]
    if mode != "image-to-video":
        raise HTTPException(503, "This free build currently renders Image to Video with Wan 2.2. The other studio modes are being connected separately.")
    if source is None:
        raise HTTPException(400, "Upload an image first.")

    ext = Path(source.filename or "image.jpg").suffix.lower()
    if ext not in {".jpg", ".jpeg", ".png", ".webp"}:
        raise HTTPException(400, "Image to Video requires JPG, PNG, or WEBP.")
    data = await source.read()
    if len(data) > 25 * 1024 * 1024:
        raise HTTPException(413, "Image is too large. Maximum 25 MB.")
    source_path = UPLOADS / f"{job_id}_source{ext}"
    source_path.write_bytes(data)

    duration = max(1, min(int(duration), 5))
    JOBS[job_id] = {
        "id": job_id,
        "mode": mode,
        "prompt": prompt,
        "duration": duration,
        "identity_strength": max(0, min(identity_strength, 100)),
        "status": "starting",
        "progress": 2,
        "engine": "Wan 2.2 14B · ZeroGPU",
    }
    threading.Thread(target=run_wan, args=(job_id, source_path, prompt, duration), daemon=True).start()
    return JOBS[job_id]

@app.get("/api/jobs/{job_id}")
def get_job(job_id: str):
    if job_id not in JOBS:
        raise HTTPException(404, "Job not found")
    return JOBS[job_id]

@app.exception_handler(Exception)
async def unhandled(_, exc: Exception):
    return JSONResponse({"detail": str(exc)}, status_code=500)

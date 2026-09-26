import os
import uuid
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles

BASE = Path(__file__).resolve().parent
STATIC = BASE / "static"
UPLOADS = BASE / "uploads"
UPLOADS.mkdir(exist_ok=True)

app = FastAPI(title="ONYX Motion Studio", version="1.0.0")
app.mount("/static", StaticFiles(directory=STATIC), name="static")

JOBS: dict[str, dict] = {}

@app.get("/")
def index():
    return FileResponse(STATIC / "index.html")

@app.get("/health")
def health():
    return {
        "ok": True,
        "service": "onyx-motion-studio",
        "provider_configured": bool(os.getenv("AI_PROVIDER_URL") and os.getenv("AI_PROVIDER_KEY")),
    }

@app.get("/api/capabilities")
def capabilities():
    return {
        "image_to_video": {
            "free_prompt": True,
            "custom_duration": True,
            "identity_reference": True,
            "extend_long_video": True,
        },
        "video_face_swap": {
            "multi_person_detection": True,
            "target_selection": True,
            "tracking": True,
            "identity_lock": True,
            "preview": True,
        },
        "talking_video": {
            "text": True,
            "audio": True,
            "lip_sync": True,
        },
        "provider_configured": bool(os.getenv("AI_PROVIDER_URL") and os.getenv("AI_PROVIDER_KEY")),
    }

@app.post("/api/jobs")
async def create_job(
    mode: Literal["image-to-video", "face-swap", "talking-video"] = Form(...),
    prompt: str = Form(""),
    duration: int = Form(10),
    identity_strength: int = Form(90),
    source: UploadFile | None = File(None),
    reference: UploadFile | None = File(None),
):
    duration = max(1, min(duration, 600))
    identity_strength = max(0, min(identity_strength, 100))
    job_id = uuid.uuid4().hex[:12]

    saved = {}
    for label, upload in (("source", source), ("reference", reference)):
        if upload:
            ext = Path(upload.filename or "").suffix[:10]
            dest = UPLOADS / f"{job_id}_{label}{ext}"
            data = await upload.read()
            if len(data) > 500 * 1024 * 1024:
                raise HTTPException(413, "File too large")
            dest.write_bytes(data)
            saved[label] = dest.name

    configured = bool(os.getenv("AI_PROVIDER_URL") and os.getenv("AI_PROVIDER_KEY"))
    status = "queued" if configured else "provider_required"
    JOBS[job_id] = {
        "id": job_id,
        "mode": mode,
        "prompt": prompt,
        "duration": duration,
        "identity_strength": identity_strength,
        "files": saved,
        "status": status,
        "progress": 0,
    }
    return JOBS[job_id]

@app.get("/api/jobs/{job_id}")
def get_job(job_id: str):
    if job_id not in JOBS:
        raise HTTPException(404, "Job not found")
    return JOBS[job_id]

@app.exception_handler(Exception)
async def unhandled(_, exc: Exception):
    return JSONResponse({"detail": str(exc)}, status_code=500)

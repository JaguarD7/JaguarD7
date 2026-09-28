from fastapi import FastAPI
from fastapi.responses import PlainTextResponse

app = FastAPI(title="LAQTA - Stopped")

@app.api_route("/{path:path}", methods=["GET","POST","PUT","PATCH","DELETE","OPTIONS"])
async def stopped(path: str):
    return PlainTextResponse("LAQTA has been permanently stopped.", status_code=410)

from fastapi import FastAPI
from fastapi.responses import JSONResponse

app = FastAPI()

@app.api_route("/{path:path}", methods=["GET","POST","PUT","PATCH","DELETE","OPTIONS"])
async def stopped(path: str):
    return JSONResponse(status_code=410, content={"status":"stopped","message":"LAQTA has been permanently disabled"})

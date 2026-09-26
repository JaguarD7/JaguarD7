import os, urllib.request
token=os.environ.get("LAQTA_CRON_TOKEN","")
if not token:
    raise SystemExit("LAQTA_CRON_TOKEN missing")
req=urllib.request.Request(
    "https://laqta-control-center.onrender.com/api/cron",
    data=b"{}",
    method="POST",
    headers={"X-Cron-Token":token,"Content-Type":"application/json"},
)
with urllib.request.urlopen(req,timeout=90) as r:
    print(r.read().decode())

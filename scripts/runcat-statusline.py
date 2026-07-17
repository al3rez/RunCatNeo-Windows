#!/usr/bin/env python3
"""
RunCat Neo for Windows - Claude Code statusLine integration.

Claude Code invokes this script each turn, passing session JSON on stdin. It writes a
Custom Metrics snapshot that RunCat Neo watches, and prints the model name as the status line.

By default it writes to:
    %APPDATA%\\RunCatNeo\\Metrics\\claude-code.json   (Windows)
    ~/.config/RunCatNeo/Metrics/claude-code.json      (other platforms)
which is inside RunCat's auto-watched Metrics folder, so no "Add JSON source" step is needed.
Override with the RUNCAT_OUT_FILE environment variable.

Setup (PowerShell):
    Copy-Item runcat-statusline.py $HOME\\.claude\\runcat-statusline.py
Then in %USERPROFILE%\\.claude\\settings.json:
    {
      "statusLine": {
        "type": "command",
        "command": "python \"%USERPROFILE%\\.claude\\runcat-statusline.py\""
      }
    }
"""

import json
import os
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path


def default_out() -> Path:
    appdata = os.environ.get("APPDATA")
    base = Path(appdata) if appdata else (Path.home() / ".config")
    return base / "RunCatNeo" / "Metrics" / "claude-code.json"


OUT = Path(os.environ.get("RUNCAT_OUT_FILE", str(default_out())))


def pct(title, value):
    if value is None:
        return None
    return {"title": title, "formattedValue": f"{value:g}%", "normalizedValue": round(value / 100, 4)}


try:
    payload = json.load(sys.stdin)
    if not isinstance(payload, dict):
        payload = {}
except Exception:
    payload = {}

model = (payload.get("model") or {}).get("display_name") or "Claude Code"
ctx = (payload.get("context_window") or {}).get("used_percentage")
rate_limits = payload.get("rate_limits") or {}
five = (rate_limits.get("five_hour") or {}).get("used_percentage")
seven = (rate_limits.get("seven_day") or {}).get("used_percentage")

snapshot = {
    "title": "Claude Code",
    "symbol": "staroflife",
    "metrics": [m for m in [
        {"title": "Model", "formattedValue": model},
        pct("Context", ctx),
        pct("5h", five),
        pct("7d", seven),
    ] if m is not None],
    "lastUpdatedDate": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}
if ctx is not None:
    snapshot["metricsBarValue"] = f"{ctx:g}%"

OUT.parent.mkdir(parents=True, exist_ok=True)
# Atomic write: temp file in the same dir, then replace, so RunCat never reads a half-written file.
fd, tmp = tempfile.mkstemp(prefix=".runcat-", dir=str(OUT.parent))
with os.fdopen(fd, "w", encoding="utf-8") as f:
    json.dump(snapshot, f, ensure_ascii=False)
os.replace(tmp, OUT)

print(model)

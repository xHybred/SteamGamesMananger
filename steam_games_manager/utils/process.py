"""Steam-running guard: psutil → tasklist/pgrep fallback."""
from __future__ import annotations

import subprocess
import sys

from .win import RUN_KWARGS


def is_steam_running() -> bool:
    try:
        import psutil  # type: ignore

        for p in psutil.process_iter(["name"]):
            name = (p.info.get("name") or "").lower()
            if name in ("steam.exe", "steam"):
                return True
        return False
    except Exception:
        pass
    try:
        if sys.platform == "win32":
            proc = subprocess.run(["tasklist", "/FI", "IMAGENAME eq steam.exe"], capture_output=True, text=True, timeout=15, **RUN_KWARGS)
            return "steam.exe" in proc.stdout.lower()
        proc = subprocess.run(["pgrep", "-x", "steam"], capture_output=True, timeout=10, **RUN_KWARGS)
        return proc.returncode == 0
    except Exception:
        return False

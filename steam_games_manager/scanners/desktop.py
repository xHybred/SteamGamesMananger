"""Desktop Win32 games the launcher scanners cannot see (currently Roblox Studio).

The Roblox player needs a place id to launch, so only Studio is added. It opens
standalone with `-ide`. Windows-only: returns [] elsewhere.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

from ..models import GameInfo


def _roblox_versions() -> list[Path]:
    base = Path(os.environ.get("LOCALAPPDATA", "")) / "Roblox" / "Versions"
    if not base.is_dir():
        return []
    return sorted([d for d in base.iterdir() if d.is_dir()],
                  key=lambda d: d.stat().st_mtime, reverse=True)


def _newest_exe(dirs: list[Path], exe: str) -> Path | None:
    for d in dirs:
        cand = d / exe
        if cand.is_file():
            return cand
    return None


class DesktopScanner:
    name = "desktop"

    def scan(self) -> list[GameInfo]:
        if sys.platform != "win32":
            return []
        out: list[GameInfo] = []
        versions = _roblox_versions()
        studio = _newest_exe(versions, "RobloxStudioBeta.exe")
        if studio is not None:
            out.append(GameInfo(
                name="Roblox Studio",
                exe=str(studio),
                start_dir=str(studio.parent),
                launch_options="-ide",
                source="desktop",
                source_id="roblox-studio",
                tags=["Roblox"],
            ))
        return out

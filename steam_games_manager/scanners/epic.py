"""Epic Games Store scanner — reads `%PROGRAMDATA%\\Epic\\...\\Manifests\\*.item`.

Manifest JSON fields used: DisplayName, AppName, InstallLocation,
LaunchExecutable. Non-game entries (UE prerequisites, launcher itself)
are skipped via bIsApplication / CatalogNamespace heuristics.
"""
from __future__ import annotations

import glob
import json
import os
import sys
from pathlib import Path

from ..models import GameInfo

SKIP_NAMESPACES = {"ue", "unrealengine"}
SKIP_APP_SUFFIXES = ("_dedicatedserver",)


def manifests_dir(explicit: str | None = None) -> Path:
    if explicit:
        return Path(os.path.expandvars(explicit))
    base = os.environ.get("PROGRAMDATA", r"C:\ProgramData")
    return Path(base) / "Epic" / "EpicGamesLauncher" / "Data" / "Manifests"


def launcher_exe(explicit: str | None = None) -> str:
    if explicit:
        return explicit
    pf86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
    for cand in (
        Path(pf86) / "Epic Games" / "Launcher" / "Portal" / "Binaries" / "Win64" / "EpicGamesLauncher.exe",
        Path(pf86) / "Epic Games" / "Launcher" / "Portal" / "Binaries" / "Win32" / "EpicGamesLauncher.exe",
    ):
        if cand.is_file():
            return str(cand)
    return str(Path(pf86) / "Epic Games" / "Launcher" / "Portal" / "Binaries" / "Win64" / "EpicGamesLauncher.exe")


def catalog_path(explicit: str | None = None) -> Path:
    if explicit:
        return Path(os.path.expandvars(explicit))
    base = os.environ.get("PROGRAMDATA", r"C:\ProgramData")
    return Path(base) / "Epic" / "EpicGamesLauncher" / "Data" / "Catalog" / "catcache.bin"


def load_catalog_art(path: Path) -> dict[str, dict[str, str]]:
    """Map CatalogItemId -> {"grids": url, "heroes": url} from the launcher's cache.

    The cache is base64-encoded JSON. Only exact per-item key art is returned;
    nothing is guessed by name.
    """
    import base64

    try:
        text = path.read_bytes().decode("utf-8", errors="strict")
        items = json.loads(base64.b64decode(text).decode("utf-8"))
    except (OSError, ValueError, UnicodeDecodeError):
        return {}
    out: dict[str, dict[str, str]] = {}
    if not isinstance(items, list):
        return out
    for item in items:
        if not isinstance(item, dict) or not item.get("id"):
            continue
        images = {str(k.get("type", "")): str(k.get("url", ""))
                  for k in item.get("keyImages") or [] if isinstance(k, dict)}
        art: dict[str, str] = {}
        if images.get("DieselGameBoxTall"):
            art["grids"] = images["DieselGameBoxTall"]
        elif images.get("DieselGameBox"):
            art["grids"] = images["DieselGameBox"]
        if images.get("DieselGameBox"):
            art["heroes"] = images["DieselGameBox"]
        if art:
            out[str(item["id"])] = art
    return out


def _is_game_manifest(data: dict) -> bool:
    app = str(data.get("AppName", ""))
    if not app or app.lower().endswith(SKIP_APP_SUFFIXES):
        return False
    categories = data.get("AppCategories")
    if isinstance(categories, list) and categories and "games" not in categories:
        return False  # engines, plugins, hidden content, and other non-games
    ns = str(data.get("CatalogNamespace", "")).lower()
    if ns in SKIP_NAMESPACES:
        return False
    if data.get("bIsApplication") is False:
        return False
    if not data.get("DisplayName") or not data.get("InstallLocation"):
        return False
    return True


def parse_manifest(path: Path, launcher: str,
                   catalog: dict[str, dict[str, str]] | None = None) -> GameInfo | None:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None
    if not isinstance(data, dict) or not _is_game_manifest(data):
        return None
    name = str(data.get("DisplayName", "")).strip()
    app = str(data.get("AppName", "")).strip()
    install = str(data.get("InstallLocation", "")).strip()
    if not name or not app:
        return None
    # Correct protocol form: com.epicgames.launcher://apps/<AppName>?action=launch&silent=true
    art = dict((catalog or {}).get(str(data.get("CatalogItemId", "")), {}))
    return GameInfo(
        name=name,
        exe=launcher,
        start_dir=install,
        launch_options=f"com.epicgames.launcher://apps/{app}?action=launch&silent=true",
        source="epic",
        source_id=app,
        tags=["Epic"],
        art_urls=art,
    )


class EpicScanner:
    name = "epic"

    def __init__(self, manifests: str | None = None, launcher: str | None = None) -> None:
        self._manifests = manifests
        self._launcher = launcher

    def scan(self) -> list[GameInfo]:
        if sys.platform != "win32" and self._manifests is None:
            return []
        mdir = manifests_dir(self._manifests)
        launcher = launcher_exe(self._launcher)
        if not mdir.is_dir():
            return []
        out: list[GameInfo] = []
        catalog = load_catalog_art(catalog_path())
        for f in sorted(glob.glob(str(mdir / "*.item"))):
            g = parse_manifest(Path(f), launcher, catalog)
            if g is not None:
                out.append(g)
        return out

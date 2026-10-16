"""Ubisoft Connect and EA App games from their registry install records.

Ubisoft: HKLM\\SOFTWARE\\(WOW6432Node\\)Ubisoft\\Launcher\\Installs\\<id>\\InstallDir
EA App:  Uninstall entries whose install folder holds an EAInstaller / Cleanup.exe
         marker (same heuristic as DLSS Swapper's EAAppLibrary).

Launch target is the game's main executable, picked by size after excluding
helpers, crash reporters, installers, and anti-cheat binaries.
"""
from __future__ import annotations

import difflib
import functools
import hashlib
import json
import math
import os
import re
import sys
import tempfile
from pathlib import Path

from ..models import GameInfo
from ..utils.icon import exe_icon_png

EXCLUDED_EXE_TOKENS = (
    "setup", "unins", "uninstall", "crash", "redist", "helper", "cleanup",
    "dxsetup", "vc_redist", "_be", "eac", "bootstrap", "updater", "launcher",
    "sen_launcher", "eaanticheat", "installer",
)


def pick_main_exe(folder: Path) -> Path | None:
    """Largest plausible game executable directly inside `folder`."""
    try:
        candidates = [p for p in folder.iterdir() if p.is_file() and p.suffix.lower() == ".exe"]
    except OSError:
        return None
    usable = [p for p in candidates
              if not any(tok in p.name.lower() for tok in EXCLUDED_EXE_TOKENS)]
    if not usable:
        return None
    return max(usable, key=lambda p: p.stat().st_size)


def _reg_values(key_path: str, names: tuple[str, ...]) -> list[dict[str, str]]:
    """Read named values for every subkey of an HKLM path (both registry views)."""
    try:
        import winreg
    except ImportError:
        return []
    rows: list[dict[str, str]] = []
    for view in (winreg.KEY_WOW64_32KEY, winreg.KEY_WOW64_64KEY):
        try:
            base = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, key_path, 0,
                                  winreg.KEY_READ | view)
        except OSError:
            continue
        with base:
            i = 0
            while True:
                try:
                    sub = winreg.EnumKey(base, i)
                except OSError:
                    break
                i += 1
                try:
                    with winreg.OpenKey(base, sub) as k:
                        row: dict[str, str] = {"_key": sub}
                        for n in names:
                            try:
                                v, _ = winreg.QueryValueEx(k, n)
                                row[n] = str(v)
                            except OSError:
                                row[n] = ""
                        rows.append(row)
                except OSError:
                    continue
    return rows


UBISOFT_ASSETS = "https://ubistatic3-a.akamaihd.net/orbit/uplay_launcher_3_0/assets/"


def _ubisoft_records(data: bytes) -> list[tuple[int, int, int]]:
    """(install_id, offset, size) per YAML record in the configurations file.

    Port of the header walk in DLSS Swapper's UbisoftConnectLibrary (after lutris' parser).
    """
    def convert(value: int) -> int:
        if value > 256 * 256:
            value -= 128 * 256 * math.ceil(value / (256.0 * 256.0))
            value -= 128 * math.ceil(value / 256.0)
        elif value > 256:
            value -= 128 * math.ceil(value / 256.0)
        return value

    def header(buf: bytes, second: bool = False) -> tuple[int, int, int, int]:
        try:
            offset, mult, size, tmp = 1, 1, 0, 0
            if second:
                while buf[offset] != 0x08 or (buf[offset] == 0x08 and buf[offset + 1] == 0x08):
                    size += buf[offset] * mult
                    mult *= 256
                    offset += 1
                    tmp += 1
            else:
                while buf[offset] != 0x08 or size == 0:
                    size += buf[offset] * mult
                    mult *= 256
                    offset += 1
                    tmp += 1
            size = convert(size)
            offset += 1
            mult, launch = 1, 0
            while buf[offset] != 0x10 or buf[offset + 1] == 0x10:
                launch += buf[offset] * mult
                mult *= 256
                offset += 1
            launch = convert(launch)
            offset += 1
            mult, launch2 = 1, 0
            while buf[offset] != 0x1A or (buf[offset] == 0x1A and buf[offset + 1] == 0x1A):
                launch2 += buf[offset] * mult
                mult *= 256
                offset += 1
            launch2 = convert(launch2)
            if size - offset < 128 and 120 <= size:
                tmp -= 1
                size += 1
            return size - offset, launch, launch2, offset + tmp + 1
        except IndexError:
            return 0, 0, 0, 10

    records: list[tuple[int, int, int]] = []
    pos = 0
    while pos < len(data):
        chunk = data[pos:]
        size, install_id, _launch, head = header(chunk)
        if size > 500:
            records.append((install_id, pos + head, size))
        before = pos
        pos += size + head
        if pos < len(data) and data[pos] != 0x0A:
            size2, _i, _l, head2 = header(chunk, True)
            pos = before + size2 + head2
    return records


UBISOFT_ART_FIELDS = (("thumb_image", "grids"), ("logo_image", "logos"))


def _ubisoft_asset(blob: str, field: str) -> str:
    """CDN URL for one image field; a bare key is resolved through the record's default localizations."""
    m = re.search(rf"^\s*{field}:\s*(\S.*?)\s*$", blob, re.MULTILINE)
    if not m:
        return ""
    value = m.group(1).strip().strip('"\'')
    if value.lower().endswith((".jpg", ".png")):
        return UBISOFT_ASSETS + value
    loc = re.search(r"^\s+" + re.escape(value) + r":\s*(\S+)\s*$", blob, re.MULTILINE)
    return UBISOFT_ASSETS + loc.group(1).strip().strip('"\'') if loc else ""


def _ubisoft_art() -> dict[int, dict[str, str]]:
    """install_id -> art URLs by kind, from Ubisoft Connect's local configurations cache."""
    local = Path(os.environ.get("LOCALAPPDATA", "")) / "Ubisoft Game Launcher" / "cache" / "configuration" / "configurations"
    if not local.is_file():
        return {}
    try:
        data = local.read_bytes()
    except OSError:
        return {}
    out: dict[int, dict[str, str]] = {}
    for install_id, offset, size in _ubisoft_records(data):
        if install_id in out:
            continue
        blob = data[offset:offset + size].decode("utf-8", errors="replace")
        art = {kind: url for field, kind in UBISOFT_ART_FIELDS if (url := _ubisoft_asset(blob, field))}
        if art:
            out[install_id] = art
    return out


def scan_ubisoft() -> list[GameInfo]:
    if sys.platform != "win32":
        return []
    out: list[GameInfo] = []
    seen_dirs: set[str] = set()
    art_by_id = _ubisoft_art()
    for row in _reg_values(r"SOFTWARE\Ubisoft\Launcher\Installs", ("InstallDir",)):
        folder = Path(row.get("InstallDir", "").strip())
        if not folder.is_dir():
            continue
        key = str(folder).lower()
        if key in seen_dirs:
            continue
        seen_dirs.add(key)
        name = folder.name.strip()
        if name.upper().endswith(" TS"):
            continue  # Ubisoft's Test Server build of the same game
        exe = pick_main_exe(folder)
        if exe is None:
            continue
        art = art_by_id.get(int(row["_key"]), {}) if row["_key"].isdigit() else {}
        out.append(GameInfo(name=name, exe=str(exe), start_dir=str(folder),
                            source="ubisoft", source_id=row["_key"], tags=["Ubisoft"],
                            art_urls=dict(art)))
    return out


def _norm_title(title: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", title.lower()))


@functools.lru_cache(maxsize=1)
def _ea_catalog() -> tuple[dict, ...]:
    """DLSS Swapper's EA App title list (ea_app_titles.json), bundled with the app."""
    cands = []
    if getattr(sys, "frozen", False):
        cands.append(Path(sys._MEIPASS) / "steam_games_manager" / "data" / "ea_app_titles.json")
    cands.append(Path(__file__).resolve().parent.parent / "data" / "ea_app_titles.json")
    for cand in cands:
        if cand.is_file():
            try:
                data = json.loads(cand.read_text(encoding="utf-8"))
            except ValueError:
                return ()
            return tuple(d for d in data if isinstance(d, dict))
    return ()


def _catalog_url(entry: dict, key: str) -> str:
    value = str((entry.get(key) or {}).get("path") or "")
    return value if value.startswith("http") else ""


def _ea_art(name: str) -> dict[str, str]:
    """Cover art from the title list: exact title first, then the closest title at 60% or more."""
    want = _norm_title(name)
    if not want:
        return {}
    best: dict | None = None
    best_score = 0.0
    for entry in _ea_catalog():
        title = _norm_title(str(entry.get("title", "")))
        if not title:
            continue
        if title == want:
            best, best_score = entry, 1.0
            break
        score = difflib.SequenceMatcher(None, want, title).ratio()
        if score > best_score:
            best, best_score = entry, score
    if best is None or best_score < 0.6:
        return {}
    art: dict[str, str] = {}
    pack = _catalog_url(best, "packArtImage")
    key = _catalog_url(best, "keyArtImage")
    logo = _catalog_url(best, "logoImage")
    if pack or key:
        art["grids"] = pack or key
    if key:
        art["heroes"] = key
    if logo:
        art["logos"] = logo
    return art


def _exe_icon(exe: Path) -> dict[str, str]:
    """Last resort (DLSS Swapper's EA fallback): the game exe's own icon."""
    dest = Path(tempfile.gettempdir()) / "sgm_icons" / f"{hashlib.sha1(str(exe).encode()).hexdigest()}.png"
    try:
        if exe_icon_png(str(exe), dest):
            return {"icons": str(dest)}
    except (OSError, ValueError, ImportError):
        pass
    return {}


def scan_ea() -> list[GameInfo]:
    if sys.platform != "win32":
        return []
    out: list[GameInfo] = []
    uninstall = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
    for row in _reg_values(uninstall, ("DisplayName", "InstallLocation")):
        name = row.get("DisplayName", "").strip()
        folder = Path(row.get("InstallLocation", "").strip().strip('"'))
        if not name or not folder.is_dir():
            continue
        lowered = name.lower()
        if "ea app" in lowered or "ea desktop" in lowered or "origin" in lowered:
            continue  # the launcher itself
        if not ((folder / "EAInstaller").exists() or (folder / "Cleanup.exe").exists()):
            continue
        exe = pick_main_exe(folder)
        if exe is None:
            continue
        art = _ea_art(name) or _exe_icon(exe)
        out.append(GameInfo(name=name, exe=str(exe), start_dir=str(folder),
                            source="ea", source_id=row["_key"], tags=["EA"],
                            art_urls=art))
    return out

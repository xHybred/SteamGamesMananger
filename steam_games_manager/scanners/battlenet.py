"""Battle.net games from the agent's product.db (protobuf) and the client's uninstall record.

Schema follows DLSS Swapper's battle_net_product_db.proto:
    ProductDb.product_installs = 1 (ProductInstall)
    ProductInstall.uid = 1, settings = 3, cached_product_state = 4
    UserSettings.install_path = 1
    CachedProductState.base_product_state = 1
    BaseProductState.installed = 1
Only the fields above are read, so a small wire-format reader is enough (no protobuf package).

Launch: Battle.net.exe --exec="launch <launcher id>". Games without a known
launcher id are skipped because they cannot be started from a shortcut.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import sys
import tempfile
from pathlib import Path

from ..models import GameInfo

# uid -> (launcher id, display title). Mirrors DLSS Swapper's BattleNetLibrary table.
KNOWN_GAMES: dict[str, tuple[str, str]] = {
    "rtro": ("rtro", "Blizzard Arcade Collection"),
    "auks": ("auks", "Call of Duty"),
    "wlby": ("wlby", "Crash Bandicoot 4: It's About Time"),
    "w1r": ("w1r", "Warcraft I: Remastered"),
    "diablo3": ("d3", "Diablo III"),
    "aris": ("aris", "Doom: The Dark Ages"),
    "heroes": ("hero", "Heroes of the Storm"),
    "d3cn": ("d3cn", "暗黑破壞神III"),
    "aqua": ("aqua", "Avowed"),
    "s2": ("s2", "StarCraft II"),
    "w2": ("w2bn", "Warcraft II: Battle.net Edition"),
    "fenris": ("fenris", "Diablo IV"),
    "d1": ("drtl", "Diablo"),
    "scor": ("scor", "Sea of Thieves"),
    "w3": ("w3", "Warcraft III: Reforged"),
    "fore": ("fore", "Call of Duty: Vanguard"),
    "s1": ("s1", "StarCraft"),
    "osi": ("osi", "Diablo II: Resurrected"),
    "lazarus": ("lazr", "Call of Duty: MW2 Campaign Remastered"),
    "odin": ("odin", "Call of Duty: Modern Warfare"),
    "pinta": ("pinta", "Call of Duty: Modern Warfare III"),
    "prometheus": ("pro", "Overwatch"),
    "viper": ("viper", "Call of Duty: Black Ops 4"),
    "zeus": ("zeus", "Call of Duty: Black Ops Cold War"),
    "w1": ("war1", "Warcraft: Orcs & Humans"),
    "w2r": ("w2r", "Warcraft II Remastered"),
    "hs_beta": ("hsb", "Hearthstone"),
    "lbra": ("lbra", "Tony Hawk's Pro Skater 3+4"),
    "ark": ("ark", "The Outer Worlds 2"),
    "nina": ("nina", "Call of Duty: Modern Warfare II"),
}

IGNORED_UID = re.compile(r"^(agent|agent_beta|beta|battle\.net|wow.*)$", re.IGNORECASE)
UNINSTALL_KEY = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net"


def _varint(buf: bytes, pos: int) -> tuple[int, int]:
    result = 0
    shift = 0
    while True:
        byte = buf[pos]
        pos += 1
        result |= (byte & 0x7F) << shift
        if not byte & 0x80:
            return result, pos
        shift += 7


def _fields(buf: bytes):
    """Yield (field_number, wire_type, value) for one protobuf message."""
    pos = 0
    while pos < len(buf):
        key, pos = _varint(buf, pos)
        number, wire = key >> 3, key & 0x7
        if wire == 0:
            value, pos = _varint(buf, pos)
        elif wire == 1:
            value, pos = buf[pos:pos + 8], pos + 8
        elif wire == 2:
            length, pos = _varint(buf, pos)
            value, pos = buf[pos:pos + length], pos + length
        elif wire == 5:
            value, pos = buf[pos:pos + 4], pos + 4
        else:
            raise ValueError(f"unsupported wire type {wire}")
        yield number, wire, value


def _first(buf: bytes, number: int):
    for n, _, value in _fields(buf):
        if n == number:
            return value
    return None


def _parse_install(raw: bytes) -> dict | None:
    uid = _first(raw, 1)
    settings = _first(raw, 3)
    state = _first(raw, 4)
    if not isinstance(uid, bytes) or not isinstance(settings, bytes) or not isinstance(state, bytes):
        return None
    base = _first(state, 1)
    installed = bool(_first(base, 1)) if isinstance(base, bytes) else False
    install_path = _first(settings, 1)
    product_code = _first(raw, 2)
    return {
        "uid": uid.decode("utf-8", errors="replace"),
        "product_code": product_code.decode("utf-8", errors="replace") if isinstance(product_code, bytes) else "",
        "installed": installed,
        "install_path": install_path.decode("utf-8", errors="replace") if isinstance(install_path, bytes) else "",
    }


def aggregate_logos(agent_dir: Path) -> dict[str, str]:
    """product_id -> logo_art_uri from the agent's aggregate.json (DLSS Swapper's source)."""
    try:
        data = json.loads((agent_dir / "aggregate.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    out: dict[str, str] = {}
    for item in data.get("installed", []) if isinstance(data, dict) else []:
        if isinstance(item, dict) and item.get("product_id") and item.get("logo_art_uri"):
            out[str(item["product_id"])] = str(item["logo_art_uri"])
    return out


HOSTED_COVER = "https://dlss-swapper-downloads.beeradmoore.com/images/covers/battlenet/{uid}.webp"


def product_db_path() -> Path:
    base = os.environ.get("ALLUSERSPROFILE") or os.environ.get("ProgramData", r"C:\ProgramData")
    return Path(base) / "Battle.net" / "Agent" / "product.db"


def client_exe() -> str:
    try:
        import winreg
    except ImportError:
        return ""
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, UNINSTALL_KEY, 0,
                            winreg.KEY_READ | winreg.KEY_WOW64_32KEY) as k:
            location, _ = winreg.QueryValueEx(k, "InstallLocation")
    except OSError:
        return ""
    exe = Path(str(location).strip().strip('"')) / "Battle.net.exe"
    return str(exe) if exe.is_file() else ""


def scan_battlenet() -> list[GameInfo]:
    if sys.platform != "win32":
        return []
    db = product_db_path()
    if not db.is_file():
        return []
    client = client_exe()
    if not client:
        return []
    fd, name = tempfile.mkstemp(suffix=".db")
    os.close(fd)
    tmp = Path(name)
    try:
        shutil.copyfile(db, tmp)
        raw = tmp.read_bytes()
    except OSError:
        return []
    finally:
        tmp.unlink(missing_ok=True)

    logos = aggregate_logos(db.parent)
    out: list[GameInfo] = []
    seen: set[str] = set()
    try:
        installs = [v for n, w, v in _fields(raw) if n == 1 and w == 2]
    except (ValueError, IndexError):
        return []
    for blob in installs:
        try:
            info = _parse_install(blob)
        except (ValueError, IndexError):
            continue
        if not info or not info["installed"] or IGNORED_UID.match(info["uid"]):
            continue
        if info["uid"] in seen or info["uid"] not in KNOWN_GAMES:
            continue
        folder = Path(info["install_path"].strip())
        if not info["install_path"] or not folder.is_dir():
            continue
        seen.add(info["uid"])
        launcher_id, title = KNOWN_GAMES[info["uid"]]
        cover = logos.get(info["product_code"]) or HOSTED_COVER.format(uid=info["uid"])
        out.append(GameInfo(
            name=title,
            exe=client,
            start_dir=str(folder),
            launch_options=f'--exec="launch {launcher_id}"',
            source="battlenet",
            source_id=info["uid"],
            tags=["Battle.net"],
            art_urls={"grids": cover},
        ))
    return out

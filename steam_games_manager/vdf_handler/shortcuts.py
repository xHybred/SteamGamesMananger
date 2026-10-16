"""Safe load / merge / write for Steam's binary shortcuts.vdf."""
from __future__ import annotations

import shutil
import time
from pathlib import Path
from typing import Any

from ..models import GameInfo
from . import binary_vdf
from .appid import appid_signed, appid_unsigned


def _has_vdf_module() -> bool:
    try:
        import vdf  # type: ignore

        return hasattr(vdf, "binary_loads") and hasattr(vdf, "binary_dumps")
    except Exception:
        return False


def load_shortcuts(path: Path) -> dict[str, Any]:
    """Deserialize shortcuts.vdf. Corrupt files raise ValueError; missing → empty."""
    if not path.exists():
        return {"shortcuts": {}}
    raw = path.read_bytes()
    if not raw:
        return {"shortcuts": {}}
    try:
        if _has_vdf_module():
            import vdf  # type: ignore

            data = vdf.binary_loads(raw)
        else:
            data = binary_vdf.loads(raw)
    except Exception as exc:
        raise ValueError(f"Corrupted shortcuts.vdf at {path}: {exc}") from exc
    if not isinstance(data, dict) or "shortcuts" not in data:
        raise ValueError(f"Unexpected shortcuts.vdf structure at {path}: missing 'shortcuts'")
    shortcuts = data["shortcuts"]
    if not isinstance(shortcuts, dict):
        raise ValueError(f"Unexpected shortcuts.vdf structure at {path}: 'shortcuts' not a dict")
    return {"shortcuts": dict(shortcuts)}


def dump_shortcuts(data: dict[str, Any]) -> bytes:
    if _has_vdf_module():
        import vdf  # type: ignore

        return vdf.binary_dumps(data)
    return binary_vdf.dumps(data)


def backup_file(path: Path) -> Path | None:
    if not path.exists():
        return None
    stamp = time.strftime("%Y%m%d-%H%M%S")
    dest = path.with_suffix(f".vdf.bak-{stamp}")
    shutil.copy2(path, dest)
    return dest


def to_vdf_entry(game: GameInfo, last_playtime: int = 0) -> dict[str, Any]:
    exe = game.exe.strip()
    if exe.startswith('"') and exe.endswith('"') and len(exe) >= 2:
        exe = exe[1:-1]
    return {
        "appid": appid_signed(exe, game.name),
        "AppName": game.name,
        "Exe": f'"{exe}"',
        "StartDir": f'"{game.start_dir}"' if game.start_dir else "",
        "icon": game.icon or "",
        "ShortcutPath": "",
        "LaunchOptions": game.launch_options or "",
        "IsHidden": 0,
        "AllowDesktopConfig": 1,
        "AllowOverlay": 1,
        "OpenVR": 0,
        "Devkit": 0,
        "DevkitGameID": "",
        "DevkitOverrideAppID": 0,
        "LastPlayTime": int(last_playtime),
        "tags": {str(i): t for i, t in enumerate(game.tags or [game.source])} if game.tags or game.source else {},
    }


def existing_keys(data: dict[str, Any]) -> dict[tuple[str, str], str]:
    """Map (AppName, Exe) → index key for dedup."""
    out: dict[tuple[str, str], str] = {}
    for k, v in data.get("shortcuts", {}).items():
        if isinstance(v, dict):
            out[(str(v.get("AppName", "")), str(v.get("Exe", "")))] = str(k)
    return out


def next_index(data: dict[str, Any]) -> int:
    best = -1
    for k in data.get("shortcuts", {}):
        try:
            best = max(best, int(k))
        except (TypeError, ValueError):
            continue
    return best + 1


def merge_games(
    data: dict[str, Any], games: list[GameInfo], overwrite: bool = False
) -> tuple[dict[str, Any], dict[str, int]]:
    """Append/update shortcuts in memory. Returns (data, stats)."""
    stats = {"added": 0, "updated": 0, "skipped": 0}
    lookup = existing_keys(data)
    idx = next_index(data)
    for g in games:
        g.normalized()
        key = (g.name, f'"{g.exe}"')
        if key in lookup:
            if overwrite:
                data["shortcuts"][lookup[key]] = to_vdf_entry(g)
                stats["updated"] += 1
            else:
                stats["skipped"] += 1
            continue
        data["shortcuts"][str(idx)] = to_vdf_entry(g)
        idx += 1
        stats["added"] += 1
    return data, stats


def write_shortcuts(path: Path, data: dict[str, Any]) -> Path | None:
    """Atomic write with backup. Returns backup path (or None if new file)."""
    path.parent.mkdir(parents=True, exist_ok=True)
    bak = backup_file(path)
    raw = dump_shortcuts(data)
    tmp = path.with_suffix(".vdf.tmp")
    tmp.write_bytes(raw)
    tmp.replace(path)
    return bak


def grid_appid_for(game: GameInfo) -> int:
    exe = game.exe.strip().strip('"')
    return appid_unsigned(exe, game.name)


import re as _re

from ..scanners.xbox import SKIP_FAMILIES as _SYSTEM_PREFIXES

_GUID_RE = _re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")


def is_system_entry(entry: dict[str, Any]) -> bool:
    """True for UWP/OS junk the old Xbox scanner imported (GUID names, system families)."""
    if not isinstance(entry, dict):
        return False
    name = str(entry.get("AppName", "")).strip()
    if not name or _GUID_RE.match(name):
        return True
    return name.startswith(_SYSTEM_PREFIXES)


def prune_system_entries(data: dict[str, Any]) -> tuple[dict[str, Any], int]:
    """Remove system/UWP junk entries in memory. Returns (data, removed_count)."""
    doomed = [k for k, v in data.get("shortcuts", {}).items() if is_system_entry(v)]
    for k in doomed:
        del data["shortcuts"][k]
    return data, len(doomed)

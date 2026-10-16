"""Titles already installed in the user's Steam libraries.

Reads `steamapps\\libraryfolders.vdf` for every library root, then each
`appmanifest_<appid>.acf` for the installed game's name. Used to mark games
that are already owned/installed on Steam, so the user isn't shown them as
new non-Steam candidates.
"""
from __future__ import annotations

import re
from pathlib import Path

import vdf  # type: ignore

_NON_GAME_APPIDS = {"228980"}  # Steamworks Common Redistributables


def normalize_title(title: str) -> str:
    return " ".join(re.findall(r"[a-z0-9]+", title.lower()))


def library_roots(steam_dir: Path) -> list[Path]:
    """All steamapps folders: the main one plus any extra libraries."""
    base = steam_dir / "steamapps"
    roots = [base]
    vdf_path = base / "libraryfolders.vdf"
    try:
        data = vdf.load(open(vdf_path, encoding="utf-8", errors="replace"))
    except (OSError, ValueError, SyntaxError):
        return roots
    folders = data.get("libraryfolders", data) if isinstance(data, dict) else {}
    for entry in folders.values() if isinstance(folders, dict) else []:
        if isinstance(entry, dict) and entry.get("path"):
            extra = Path(str(entry["path"])) / "steamapps"
            if extra.is_dir() and extra not in roots:
                roots.append(extra)
    return roots


def installed_titles(steam_dir: Path | None) -> set[str]:
    """Normalized names of games installed in any Steam library."""
    if steam_dir is None or not steam_dir.is_dir():
        return set()
    titles: set[str] = set()
    for root in library_roots(steam_dir):
        for acf in root.glob("appmanifest_*.acf"):
            try:
                data = vdf.load(open(acf, encoding="utf-8", errors="replace"))
            except (OSError, ValueError, SyntaxError):
                continue
            state = data.get("AppState", {}) if isinstance(data, dict) else {}
            appid = str(state.get("appid", ""))
            name = str(state.get("name", "")).strip()
            if not name or appid in _NON_GAME_APPIDS:
                continue
            titles.add(normalize_title(name))
    return titles

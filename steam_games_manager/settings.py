"""Persistent GUI/CLI settings (JSON next to the exe, %APPDATA% fallback)."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

APP_NAME = "SteamGamesManager"
FILE_NAME = "settings.json"

DEFAULT_SOURCES = {"epic": True, "gog": True, "xbox": False, "ubisoft": True, "ea": True,
                   "battlenet": True, "desktop": True}


def defaults() -> dict:
    return {
        "sources": dict(DEFAULT_SOURCES),
        "view": "list",
        "density": "compact",
        "artwork": True,
        "overwrite": True,
        "force": False,
        "api_key": "",
        "hidden": [],  # list of [source, name, exe]
    }


def settings_path() -> Path:
    name = FILE_NAME
    if getattr(sys, "frozen", False):
        exe_dir = Path(sys.executable).resolve().parent
        try:
            probe = exe_dir / ".sgm_write_test"
            probe.touch()
            probe.unlink()
            return exe_dir / name
        except OSError:
            pass
    else:
        cwd = Path.cwd() / name
        try:
            probe = cwd.parent / ".sgm_write_test"
            probe.touch()
            probe.unlink()
            return cwd
        except OSError:
            pass
    base = os.environ.get("APPDATA", str(Path.home()))
    d = Path(base) / APP_NAME
    d.mkdir(parents=True, exist_ok=True)
    return d / name


def load(path: Path | None = None) -> dict:
    state = defaults()
    p = path or settings_path()
    try:
        raw = json.loads(p.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return state
    if not isinstance(raw, dict):
        return state
    if isinstance(raw.get("sources"), dict):
        for k, v in DEFAULT_SOURCES.items():
            state["sources"][k] = bool(raw["sources"].get(k, v))
    for key in ("view", "density", "artwork", "overwrite", "force", "api_key"):
        if key in raw:
            state[key] = raw[key]
    if state["view"] not in ("list", "grid"):
        state["view"] = "list"
    if state.get("density") not in ("compact", "normal"):
        state["density"] = "compact"  # legacy "roomy" maps here: same row size
    if isinstance(raw.get("hidden"), list):
        state["hidden"] = [h for h in raw["hidden"] if isinstance(h, list) and len(h) == 3]
    return state


def save(state: dict, path: Path | None = None) -> Path:
    p = path or settings_path()
    p.parent.mkdir(parents=True, exist_ok=True)
    try:
        current = json.loads(p.read_text(encoding="utf-8"))
        if isinstance(current, dict):
            current.update(state)
            state = current
    except (OSError, ValueError):
        pass
    p.write_text(json.dumps(state, indent=2), encoding="utf-8")
    return p

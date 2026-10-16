"""Xbox / Game Pass (GDK) games via the `.GamingRoot` marker.

Game Pass PC titles are Win32 GDK games installed into a "gaming root" folder,
not AppX packages. Each drive root may contain a `.GamingRoot` file (header
"RGBX") that names that folder. Every game folder there has
`Content\\MicrosoftGame.config` describing its display name and executables.
Same approach as DLSS Swapper's XboxLibrary.
"""
from __future__ import annotations

import os
import string
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from ..models import GameInfo


def _gaming_root_targets() -> list[Path]:
    targets: list[Path] = []
    for letter in string.ascii_uppercase:
        drive = Path(f"{letter}:\\")
        marker = drive / ".GamingRoot"
        try:
            if not marker.is_file():
                continue
            data = marker.read_bytes()
        except OSError:
            continue
        if len(data) <= 5 or data[:4] != b"RGBX":
            continue
        # Payload after the 4-byte header + 1 separator byte is the folder path,
        # stored as one character per byte with zero bytes ignored.
        rel = "".join(chr(b) for b in data[5:] if b != 0).strip()
        if not rel:
            continue
        target = Path(str(drive) + rel)
        if target.is_dir():
            targets.append(target)
    return targets


def _parse_game_config(config: Path) -> tuple[str, str] | None:
    """Return (display_name, executable_path) or None if not a usable game."""
    try:
        root = ET.parse(config).getroot()
    except (ET.ParseError, OSError):
        return None
    if root.tag.split("}")[-1] != "Game":
        return None
    display = ""
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "ShellVisuals" and el.get("DefaultDisplayName"):
            display = el.get("DefaultDisplayName", "").strip()
            break
    exe_name = ""
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "Executable" and el.get("Name"):
            exe_name = el.get("Name", "").strip()
            break
    if not exe_name:
        return None
    exe = config.parent / exe_name
    if not exe.is_file():
        return None  # the launcher config points at a file that isn't there
    return (display or config.parent.parent.name), str(exe)


def _identity_name(config: Path) -> str:
    try:
        root = ET.parse(config).getroot()
    except (ET.ParseError, OSError):
        return ""
    for el in root.iter():
        if el.tag.split("}")[-1] == "Identity" and el.get("Name"):
            return el.get("Name", "").strip()
    return ""


def _config_art(config: Path) -> dict[str, str]:
    """Art files named in MicrosoftGame.config, resolved against the game folder.

    Order follows DLSS Swapper's XboxLibrary: splash screen first, then the square logos.
    """
    try:
        root = ET.parse(config).getroot()
    except (ET.ParseError, OSError):
        return {}
    shell = next((el for el in root.iter() if el.tag.split("}")[-1] == "ShellVisuals"), None)
    if shell is None:
        return {}
    base = config.parent

    def existing(key: str) -> str:
        rel = (shell.get(key) or "").strip()
        return str(base / rel) if rel and (base / rel).is_file() else ""

    square = existing("Square480x480Logo") or existing("Square150x150Logo")
    splash = existing("SplashScreenImage")
    art: dict[str, str] = {}
    if square:
        art["squares"] = square
    elif splash:
        art["grids"] = splash
    if splash:
        art["heroes"] = splash
    return art


def gaming_root_art() -> dict[str, dict[str, str]]:
    """Local art keyed by package identity name, for titles the AppX scan lists."""
    if sys.platform != "win32" and os.environ.get("SGM_TEST_GAMING_ROOT") is None:
        return {}
    out: dict[str, dict[str, str]] = {}
    for root in _gaming_root_targets():
        try:
            entries = list(root.iterdir())
        except OSError:
            continue
        for game_dir in entries:
            config = game_dir / "Content" / "MicrosoftGame.config"
            if not config.is_file():
                continue
            identity = _identity_name(config).lower()
            art = _config_art(config)
            if identity and art:
                out[identity] = art
    return out


def scan_gaming_roots(skip_names: set[str] | None = None) -> list[GameInfo]:
    """Unpackaged GDK games. Titles whose identity name is in `skip_names` are
    already listed through their registered AppX package, so they are left out here."""
    if sys.platform != "win32" and os.environ.get("SGM_TEST_GAMING_ROOT") is None:
        return []
    skip = {n.lower() for n in (skip_names or set())}
    out: list[GameInfo] = []
    for root in _gaming_root_targets():
        try:
            entries = list(root.iterdir())
        except OSError:
            continue
        for game_dir in entries:
            config = game_dir / "Content" / "MicrosoftGame.config"
            if not config.is_file():
                continue
            parsed = _parse_game_config(config)
            if parsed is None:
                continue
            if _identity_name(config).lower() in skip:
                continue
            name, exe = parsed
            out.append(GameInfo(
                name=name,
                exe=exe,
                start_dir=str(config.parent),
                launch_options="",
                source="xbox",
                source_id=game_dir.name,
                tags=["Xbox", "Game Pass"],
                art_urls=_config_art(config),
            ))
    return out

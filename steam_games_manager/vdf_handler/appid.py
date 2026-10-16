"""Canonical Steam non-Steam shortcut AppID calculation.

Algorithm (as used by Steam ROM Manager / steam-shortcut-editor / SteamDB):
    key   = exe + appname   (unquoted exe + app name, UTF-8)
    crc   = crc32(key) & 0xFFFFFFFF
    appid = crc | 0x80000000        (unsigned 32-bit, top bit set)

Steam stores a *signed* 32-bit `appid` inside shortcuts.vdf; the grid
folder uses the *unsigned* decimal form.

Grid filenames for unsigned appid N:
    Np.jpg       600x900 vertical cover
    N_hero.jpg   1920x620 hero/banner
    N_logo.png   logo
    N.ico        icon (cached locally; referenced by `icon` field)
"""
from __future__ import annotations

import zlib


def _key(exe: str, app_name: str) -> bytes:
    return (exe + app_name).encode("utf-8", errors="surrogateescape")


def appid_unsigned(exe: str, app_name: str) -> int:
    """Unsigned 32-bit AppID used for grid filenames."""
    crc = zlib.crc32(_key(exe, app_name)) & 0xFFFFFFFF
    return (crc | 0x80000000) & 0xFFFFFFFF


def appid_signed(exe: str, app_name: str) -> int:
    """Signed 32-bit form stored in shortcuts.vdf `appid` field."""
    u = appid_unsigned(exe, app_name)
    return u - 0x100000000 if u >= 0x80000000 else u


def shortcut_id_64(exe: str, app_name: str) -> int:
    """64-bit shortcut id (`(appid << 32) | 0x02000000`). Rarely needed."""
    return ((appid_unsigned(exe, app_name) << 32) | 0x02000000) & 0xFFFFFFFFFFFFFFFF

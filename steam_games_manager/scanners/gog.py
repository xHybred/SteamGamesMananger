"""GOG Galaxy scanner — registry first, galaxy-2.0.db fallback.

Registry: HKLM\\SOFTWARE\\WOW6432Node\\GOG.com\\Games\\* with values
`gameID`, `gameName`, `path`. Launch target is always GalaxyClient with
`/command=runGame /gameId=<id>` (canonical order).
SQLite fallback reads `%PROGRAMDATA%\\GOG.com\\Galaxy\\storage\\galaxy-2.0.db`
table `LibraryReleases` (releaseKey like `gog_<id>` + title column).
"""
from __future__ import annotations

import json
import os
import sqlite3
import sys
from pathlib import Path

from ..models import GameInfo


def galaxy_client(explicit: str | None = None) -> str:
    if explicit:
        return explicit
    pf86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
    for cand in (
        Path(pf86) / "GOG Galaxy" / "GalaxyClient.exe",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "GOG Galaxy" / "GalaxyClient.exe",
    ):
        if cand.is_file():
            return str(cand)
    return str(Path(pf86) / "GOG Galaxy" / "GalaxyClient.exe")


def _scan_registry() -> list[tuple[str, str, str]]:
    """Return (gameId, gameName, path) triples."""
    try:
        import winreg
    except ImportError:
        return []
    rows: list[tuple[str, str, str]] = []
    for hive, sub in (
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\GOG.com\Games"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\GOG.com\Games"),
    ):
        try:
            with winreg.OpenKey(hive, sub) as base:
                i = 0
                while True:
                    try:
                        subname = winreg.EnumKey(base, i)
                    except OSError:
                        break
                    i += 1
                    try:
                        with winreg.OpenKey(base, subname) as k:
                            gid, _ = winreg.QueryValueEx(k, "gameID")
                            try:
                                gname, _ = winreg.QueryValueEx(k, "gameName")
                            except OSError:
                                gname = subname
                            try:
                                gpath, _ = winreg.QueryValueEx(k, "path")
                            except OSError:
                                gpath = ""
                            try:
                                depends, _ = winreg.QueryValueEx(k, "dependsOn")
                            except OSError:
                                depends = ""
                            if str(depends or "").strip():
                                continue  # DLC / add-on, not a standalone game
                            if not str(gpath).strip() or not Path(str(gpath)).is_dir():
                                continue  # stale registry entry, folder is gone
                            rows.append((str(gid), str(gname), str(gpath)))
                    except OSError:
                        continue
        except OSError:
            continue
    return rows


def _scan_sqlite(db: Path) -> list[tuple[str, str]]:
    """Return (gameId, title) pairs from galaxy-2.0.db. Read-only, never locks."""
    if not db.is_file():
        return []
    try:
        uri = f"file:{db}?mode=ro"
        con = sqlite3.connect(uri, uri=True, timeout=5)
        try:
            cur = con.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='LibraryReleases'"
            )
            if cur.fetchone() is None:
                return []
            rows: list[tuple[str, str]] = []
            for key, title in con.execute("SELECT releaseKey, title FROM LibraryReleases"):
                title = (title or "").strip()
                if not title:
                    continue
                gid = str(key).split("_", 1)[-1] if "_" in str(key) else str(key)
                rows.append((gid, title))
            return rows
        finally:
            con.close()
    except Exception:
        return []


def _galaxy_art(db: Path) -> dict[str, dict[str, str]]:
    """Vertical covers per game id, as DLSS Swapper reads them.

    Galaxy's own cover files (webcache) are preferred; the GamePieces cover URL is the fallback.
    Table/column names follow DLSS Swapper's GOG DatabaseTables.cs.
    """
    if not db.is_file():
        return {}
    art: dict[str, dict[str, str]] = {}
    try:
        con = sqlite3.connect(f"file:{db}?mode=ro", uri=True, timeout=5)
    except sqlite3.Error:
        return {}
    try:
        tables = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        if {"WebCache", "WebCacheResources", "WebCacheResourceTypes"} <= tables:
            row = con.execute("SELECT id FROM WebCacheResourceTypes WHERE type='verticalCover'").fetchone()
            vtype = row[0] if row else 3
            webcache = db.parent.parent / "webcache"
            query = ("SELECT w.releaseKey, w.userId, r.filename FROM WebCache w "
                     "JOIN WebCacheResources r ON r.webCacheId = w.id "
                     "WHERE r.webCacheResourceTypeId = ?")
            for key, user_id, filename in con.execute(query, (vtype,)):
                gid = str(key).split("_", 1)[-1]
                path = webcache / str(user_id) / "gog" / gid / str(filename)
                if path.is_file():
                    art.setdefault(gid, {}).setdefault("grids", str(path))
        if "GamePieces" in tables:
            row = (con.execute("SELECT id FROM GamePieceTypes WHERE type='originalImages'").fetchone()
                   if "GamePieceTypes" in tables else None)
            ptype = row[0] if row else 378
            for key, value in con.execute(
                    "SELECT releaseKey, value FROM GamePieces WHERE gamePieceTypeId = ?", (ptype,)):
                try:
                    url = str(json.loads(value).get("verticalCover") or "")
                except (ValueError, AttributeError):
                    continue
                if url.startswith("http"):
                    gid = str(key).split("_", 1)[-1]
                    art.setdefault(gid, {}).setdefault("grids", url)
    except sqlite3.Error:
        pass
    finally:
        con.close()
    return art


def db_path(explicit: str | None = None) -> Path:
    if explicit:
        return Path(os.path.expandvars(explicit))
    return Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData")) / "GOG.com" / "Galaxy" / "storage" / "galaxy-2.0.db"


class GogScanner:
    name = "gog"

    def __init__(self, client: str | None = None, db: str | None = None) -> None:
        self._client = client
        self._db = db

    def scan(self) -> list[GameInfo]:
        if sys.platform != "win32" and self._db is None:
            return []
        client = galaxy_client(self._client)
        db = db_path(self._db)
        art = _galaxy_art(db)
        out: list[GameInfo] = []
        seen: set[str] = set()
        for gid, gname, gpath in _scan_registry():
            if not gid or gid in seen:
                continue
            seen.add(gid)
            out.append(
                GameInfo(
                    name=gname.strip() or f"GOG {gid}",
                    exe=client,
                    start_dir=gpath.strip(),
                    launch_options=f"/command=runGame /gameId={gid}",
                    source="gog",
                    source_id=gid,
                    tags=["GOG"],
                    art_urls=dict(art.get(gid, {})),
                )
            )
        for gid, title in _scan_sqlite(db):
            if gid in seen:
                continue
            seen.add(gid)
            out.append(
                GameInfo(
                    name=title,
                    exe=client,
                    start_dir=str(Path(client).parent),
                    launch_options=f"/command=runGame /gameId={gid}",
                    source="gog",
                    source_id=gid,
                    tags=["GOG"],
                    art_urls=dict(art.get(gid, {})),
                )
            )
        return out

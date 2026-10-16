"""SteamGridDB artwork search + download.

Endpoints (https://www.steamgriddb.com/api/v2):
  GET /search/autocomplete/{query}      → candidate games
  GET /games/id/{gid}/grids?dimensions=600x900&types=static
  GET /games/id/{gid}/heroes?dimensions=1920x620
  GET /games/id/{gid}/logos
  GET /games/id/{gid}/icons

Files land in <userdata>/config/grid/:
  {appid}p.jpg  {appid}_hero.jpg  {appid}_logo.png  {appid}.ico
"""
from __future__ import annotations

import logging
from pathlib import Path
from urllib.parse import quote

import requests

log = logging.getLogger(__name__)
BASE = "https://www.steamgriddb.com/api/v2"
TIMEOUT = 20


class SteamGridDBError(RuntimeError):
    pass


class SteamGridDBClient:
    def __init__(self, api_key: str, session: requests.Session | None = None) -> None:
        if not api_key or not api_key.strip():
            raise ValueError("SteamGridDB API key is required")
        self.api_key = api_key.strip()
        self.session = session or requests.Session()
        self.session.headers.update({"Authorization": f"Bearer {self.api_key}"})

    def _get(self, path: str, params: dict | None = None) -> list[dict]:
        try:
            r = self.session.get(f"{BASE}{path}", params=params or {}, timeout=TIMEOUT)
        except requests.RequestException as exc:
            raise SteamGridDBError(f"Network error calling {path}: {exc}") from exc
        if r.status_code == 401:
            raise SteamGridDBError("SteamGridDB rejected the API key (401 Unauthorized)")
        if r.status_code == 429:
            raise SteamGridDBError("SteamGridDB rate limit hit (429); retry later")
        try:
            payload = r.json()
        except ValueError as exc:
            raise SteamGridDBError(f"Non-JSON response from {path}: HTTP {r.status_code}") from exc
        if not r.ok or not payload.get("success", False):
            errors = payload.get("errors") or [f"HTTP {r.status_code}"]
            raise SteamGridDBError(f"SteamGridDB {path} failed: {errors}")
        data = payload.get("data", [])
        return data if isinstance(data, list) else [data]

    def search_game(self, name: str) -> list[dict]:
        return self._get(f"/search/autocomplete/{quote(name)}")

    def validate(self) -> bool:
        """Cheap key check: any authenticated call works; 401/429 raise."""
        self._get("/search/autocomplete/steam")
        return True

    def pick_game(self, name: str, exact_only: bool = False) -> dict | None:
        """Best candidate: exact (case-insensitive) name match wins.

        With exact_only=True, return None instead of a fuzzy first result so
        we never attach the wrong game's artwork automatically.
        """
        cands = self.search_game(name)
        if not cands:
            return None
        from .auto import _norm
        want = _norm(name)
        exact = [c for c in cands if _norm(str(c.get("name", ""))) == want] if want else []
        if exact:
            return exact[0]
        return None if exact_only else cands[0]

    def assets(self, sgdb_id: int, kind: str) -> list[dict]:
        dim = {"grids": "600x900", "heroes": "1920x620"}.get(kind)
        params: dict = {"types": "static", "limit": 10}
        if dim:
            params["dimensions"] = dim
        return self._get(f"/games/id/{sgdb_id}/{kind}", params=params)

    def download(self, url: str, dest: Path) -> bool:
        try:
            with self.session.get(url, timeout=TIMEOUT, stream=True) as r:
                r.raise_for_status()
                dest.parent.mkdir(parents=True, exist_ok=True)
                tmp = dest.with_suffix(dest.suffix + ".tmp")
                with open(tmp, "wb") as fh:
                    for chunk in r.iter_content(65536):
                        if chunk:
                            fh.write(chunk)
                tmp.replace(dest)
            return True
        except requests.RequestException as exc:
            log.warning("Artwork download failed %s: %s", url, exc)
            return False


ASSET_FILES = (
    ("grids", "p.jpg", "{appid}p.jpg"),
    ("heroes", "_hero.jpg", "{appid}_hero.jpg"),
    ("logos", "_logo.png", "{appid}_logo.png"),
    ("icons", ".ico", "{appid}.ico"),
)


def fetch_artwork(
    client: SteamGridDBClient,
    game_name: str,
    appid: int,
    grid_dir: Path,
    sgdb_id: int | None = None,
    exact_only: bool = True,
) -> dict[str, Path]:
    """Download best artwork for one game. Never raises; returns saved files.

    Auto-picking uses exact name matches only (exact_only) so a wrong game
    is never attached silently; pass an explicit sgdb_id to force a choice.
    The chosen SteamGridDB game id is logged for every game.
    """
    saved: dict[str, Path] = {}
    try:
        gid = sgdb_id
        if gid is None:
            picked = client.pick_game(game_name, exact_only=exact_only)
            if picked is None:
                log.info("SteamGridDB: no exact match for %r — art skipped (pick manually)", game_name)
                return saved
            gid = int(picked["id"])
            log.info("SteamGridDB: %r -> %r (id %s)", game_name, picked.get("name"), picked.get("id"))
        for kind, _suffix, template in ASSET_FILES:
            try:
                items = client.assets(gid, kind)
            except SteamGridDBError as exc:
                log.warning("SteamGridDB %s for %r failed: %s", kind, game_name, exc)
                continue
            if not items:
                continue
            url = str(items[0].get("url", ""))
            if not url:
                continue
            dest = grid_dir / template.format(appid=appid)
            if client.download(url, dest):
                saved[kind] = dest
    except SteamGridDBError as exc:
        log.warning("Artwork lookup for %r failed: %s", game_name, exc)
    return saved

"""CLI entry: scan launchers → merge shortcuts.vdf → fetch SteamGridDB art."""
from __future__ import annotations

import argparse
import getpass
import logging
import os
import shutil
import sys
from pathlib import Path

from . import config as cfg
from .artwork.steamgriddb import SteamGridDBClient, SteamGridDBError, fetch_artwork
from .models import GameInfo
from .scanners.base import dedupe, filter_games
import re

from .scanners.desktop import DesktopScanner
from .scanners.battlenet import scan_battlenet
from .scanners.launchers import scan_ea, scan_ubisoft
from .scanners.epic import EpicScanner
from .scanners.gog import GogScanner
from .scanners.xbox import XboxScanner
from .utils.process import is_steam_running
from .vdf_handler import shortcuts as sc

log = logging.getLogger("sgm")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="steam-games-manager",
        description="Import Epic/GOG/Xbox games into Steam shortcuts.vdf with SteamGridDB artwork.",
    )
    p.add_argument("--steam-dir", default=None, help="Steam install dir (auto-detected otherwise)")
    p.add_argument("--user-id", default=None, action="append",
                   help="Steam account id (userdata/<id>). Repeat for several. Default: ALL accounts.")
    p.add_argument("--launchers", default="epic,gog,xbox,ubisoft,ea,battlenet,desktop",
                   help="Comma list: epic,gog,xbox,ubisoft,ea,battlenet,desktop")
    p.add_argument("--api-key", default=None, help="SteamGridDB API key (or $STEAMGRIDDB_API_KEY)")
    p.add_argument("--no-artwork", action="store_true", help="Skip SteamGridDB downloads")
    p.add_argument("--dry-run", action="store_true", help="Scan + report without writing")
    p.add_argument("--force", action="store_true", help="Write even if Steam is running (risky)")
    p.add_argument("--overwrite", action="store_true", help="Update already-imported shortcuts")
    p.add_argument("--prune", action="store_true",
                   help="Remove OS/system UWP junk entries from target accounts before merging")
    p.add_argument("--no-prompt", action="store_true", help="Never prompt; fail fast instead")
    p.add_argument("--include", default=None, help="Only import games whose name contains this text")
    p.add_argument("--epic-manifests", default=None, help="Override Epic Manifests dir")
    p.add_argument("--gog-db", default=None, help="Override galaxy-2.0.db path")
    p.add_argument("--gui", action="store_true", help="Launch the graphical interface")
    p.add_argument("-v", "--verbose", action="count", default=0)
    return p


def _stdin_is_tty() -> bool:
    """None-safe stdin check (windowed pythonw/PyInstaller has sys.stdin=None)."""
    try:
        return bool(sys.stdin is not None and sys.stdin.isatty())
    except Exception:
        return False


def resolve_api_key(args: argparse.Namespace) -> str | None:
    return args.api_key or os.environ.get("STEAMGRIDDB_API_KEY")


def scan_all(launchers: str, args: argparse.Namespace) -> list[GameInfo]:
    wanted = {s.strip().lower() for s in launchers.split(",") if s.strip()}
    out: list[GameInfo] = []
    if "epic" in wanted:
        try:
            found = EpicScanner(manifests=args.epic_manifests).scan()
            log.info("Epic: %d game(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("Epic scan failed: %s", exc)
    if "gog" in wanted:
        try:
            found = GogScanner(db=args.gog_db).scan()
            log.info("GOG: %d game(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("GOG scan failed: %s", exc)
    if "xbox" in wanted:
        try:
            found = XboxScanner().scan()
            log.info("Xbox/UWP: %d app(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("Xbox scan failed: %s", exc)
    if "ubisoft" in wanted:
        try:
            found = scan_ubisoft()
            log.info("Ubisoft: %d game(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("Ubisoft scan failed: %s", exc)
    if "ea" in wanted:
        try:
            found = scan_ea()
            log.info("EA: %d game(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("EA scan failed: %s", exc)
    if "battlenet" in wanted:
        try:
            found = scan_battlenet()
            log.info("Battle.net: %d game(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("Battle.net scan failed: %s", exc)
    if "desktop" in wanted:
        try:
            found = DesktopScanner().scan()
            log.info("Desktop: %d app(s)", len(found))
            out += found
        except Exception as exc:
            log.warning("Desktop scan failed: %s", exc)
    if unknown := (wanted - KNOWN_LAUNCHERS):
        log.warning("Unknown launchers ignored: %s", sorted(unknown))
    games = dedupe_titles([g.normalized() for g in out if g.name and g.exe])
    return games


KNOWN_LAUNCHERS = {"epic", "gog", "xbox", "ubisoft", "ea", "battlenet", "desktop"}


def dedupe_titles(games: list[GameInfo]) -> list[GameInfo]:
    """Same title from two storefronts: keep the first (scan order = priority)."""
    seen: set[str] = set()
    out: list[GameInfo] = []
    for g in dedupe(games):
        key = " ".join(re.findall(r"[a-z0-9]+", g.name.lower()))
        if key in seen:
            continue
        seen.add(key)
        out.append(g)
    return out


def account_label(steam_dir: Path, user_dir: Path) -> str:
    """Human label: userdata id + persona name when known."""
    uid = user_dir.name
    for u in cfg.read_login_users(steam_dir):
        if u["account_id"] == uid:
            who = u["persona"] or u["account"] or "?"
            return f"{uid} ({who})"
    return uid


def write_account(user_dir: Path, games: list[GameInfo], overwrite: bool = False,
                  prune: bool = False) -> dict:
    """Merge games into one account's shortcuts.vdf. Returns result dict."""
    res: dict = {"user_dir": user_dir, "added": 0, "updated": 0,
                 "skipped": 0, "pruned": 0, "backup": None, "error": None}
    try:
        sc_path = cfg.shortcuts_path(user_dir)
        try:
            data = sc.load_shortcuts(sc_path)
        except ValueError as exc:
            res["error"] = f"Corrupt shortcuts.vdf, skipped (restore from .bak): {exc}"
            return res
        if prune:
            data, removed = sc.prune_system_entries(data)
            res["pruned"] = removed
        data, stats = sc.merge_games(data, games, overwrite=overwrite)
        res.update(added=stats["added"], updated=stats["updated"], skipped=stats["skipped"])
        res["backup"] = sc.write_shortcuts(sc_path, data)
    except OSError as exc:
        res["error"] = f"Write failed: {exc}"
    return res


def artwork_for_account(client: SteamGridDBClient | None, games: list[GameInfo],
                        user_dir: Path, predownloaded: dict[int, dict[str, Path]] | None = None,
                        overrides: dict[str, int] | None = None
                        ) -> tuple[int, int, dict[int, dict[str, Path]]]:
    """Fetch artwork into one account's grid dir.

    SteamGridDB (keyed, best custom art) goes first when a client is given;
    the keyless automatic providers (Epic/GOG/Steam store art) then fill any
    still-missing kinds, so artwork works with no key at all.
    predownloaded maps appid → {kind: path} from a previous account; files are
    copied instead of re-downloaded. overrides maps game name → explicit
    SteamGridDB game id (from the GUI picker). Returns (ok, fail, cache).
    """
    from .artwork.auto import fetch_auto_artwork
    from .vdf_handler.shortcuts import grid_appid_for
    grid = cfg.grid_dir(user_dir)
    cache: dict[int, dict[str, Path]] = dict(predownloaded or {})
    overrides = overrides or {}
    ok = fail = 0
    for g in games:
        appid = grid_appid_for(g)
        try:
            if appid in cache and g.name not in overrides and len(cache[appid]) >= 4:
                saved: dict[str, Path] = {}
                for kind, src in cache[appid].items():
                    dest = grid / src.name
                    if dest != src:
                        shutil.copy2(src, dest)
                    saved[kind] = dest
            else:
                saved = {}
                if client is not None:
                    saved = fetch_artwork(client, g.name, appid, grid,
                                          sgdb_id=overrides.get(g.name))
                    cache[appid] = dict(saved)
                missing = {"grids", "heroes", "logos", "icons"} - set(saved)
                if missing:
                    auto_saved, _provider = fetch_auto_artwork(
                        g.name, g.source, appid, grid, missing, g.art_urls)
                    saved.update(auto_saved)
                    for kind, path in auto_saved.items():
                        cache.setdefault(appid, {})[kind] = path
            if "icons" in saved:
                try:
                    current = sc.load_shortcuts(cfg.shortcuts_path(user_dir))
                    for v in current.get("shortcuts", {}).values():
                        if isinstance(v, dict) and v.get("AppName") == g.name:
                            v["icon"] = str(saved["icons"])
                            break
                    sc.write_shortcuts(cfg.shortcuts_path(user_dir), current)
                except Exception as exc:
                    log.warning("Could not back-patch icon for %r: %s", g.name, exc)
            if saved:
                ok += 1
            else:
                fail += 1
        except SteamGridDBError as exc:
            log.warning("%s: %s", g.name, exc)
            fail += 1
    return ok, fail, cache


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.gui:
        from .gui import run_gui
        return run_gui()
    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(levelname)s %(name)s: %(message)s",
    )
    steam_dir = cfg.steam_install_dir(args.steam_dir)
    if steam_dir is None:
        log.error("Steam installation not found. Pass --steam-dir explicitly.")
        return 2
    log.info("Steam dir: %s", steam_dir)
    try:
        user_dirs = cfg.resolve_all_user_dirs(steam_dir, args.user_id)
    except FileNotFoundError as exc:
        log.error("%s", exc)
        return 2
    recent = cfg.most_recent_account_id(steam_dir)
    if recent:
        log.info("Most-recently-used account: %s", recent)
    log.info("Target accounts: %s", [account_label(steam_dir, d) for d in user_dirs])

    games = filter_games(scan_all(args.launchers, args), args.include)
    if not games and not args.prune:
        log.warning("No games detected — nothing to do.")
        return 0
    print(f"\nDetected {len(games)} game(s):")
    for g in games:
        print(f"  [{g.source}] {g.name}\n           exe={g.exe}\n           opts={g.launch_options}")

    if args.dry_run:
        for d in user_dirs:
            try:
                data = sc.load_shortcuts(cfg.shortcuts_path(d))
                print(f"Dry run [{d.name}]: would merge {len(games)} game(s) "
                      f"into {len(data.get('shortcuts', {}))} existing. No changes written.")
            except ValueError as exc:
                print(f"Dry run [{d.name}]: {exc}")
        return 0

    if is_steam_running():
        msg = "Steam appears to be RUNNING. Close Steam before writing shortcuts.vdf to avoid corruption."
        if args.force:
            log.warning("%s Proceeding due to --force.", msg)
        else:
            log.error("%s Re-run with Steam closed (or --force to override at your own risk).", msg)
            return 4

    failed = False
    for d in user_dirs:
        res = write_account(d, games, overwrite=args.overwrite, prune=args.prune)
        if res["error"]:
            log.error("[%s] %s", d.name, res["error"])
            failed = True
            continue
        print(f"[{account_label(steam_dir, d)}] +{res['added']} added, "
              f"~{res['updated']} updated, ={res['skipped']} skipped, "
              f"-{res['pruned']} pruned.")
        if res["backup"]:
            print(f"  Backup: {res['backup']}")

    if args.no_artwork or not games:
        print("Restart Steam to see new shortcuts.")
        return 1 if failed else 0

    key = resolve_api_key(args)
    if not key and not args.no_prompt and _stdin_is_tty():
        try:
            key = getpass.getpass("SteamGridDB API key (Enter for automatic art): ").strip() or None
        except (EOFError, OSError, KeyboardInterrupt):
            key = None
    client = None
    if key:
        try:
            client = SteamGridDBClient(key)
        except ValueError as exc:
            log.error("%s", exc)
            return 2
    else:
        print("No SteamGridDB key — using fully automatic store artwork.")

    cache: dict[int, dict[str, Path]] = {}
    for d in user_dirs:
        ok, fail, cache = artwork_for_account(client, games, d, predownloaded=cache)
        print(f"[{d.name}] Artwork: {ok} succeeded, {fail} without art. "
              f"Grid dir: {cfg.grid_dir(d)}")
    print("Restart Steam to see new shortcuts and artwork.")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())

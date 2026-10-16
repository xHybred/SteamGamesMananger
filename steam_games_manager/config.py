"""Steam install / userdata / grid-directory resolution (Windows + Linux)."""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path


def steam_install_dir(explicit: str | None = None) -> Path | None:
    """Locate the Steam installation directory."""
    if explicit:
        p = Path(os.path.expandvars(explicit)).expanduser()
        if (p / "userdata").is_dir():
            return p
    # Windows registry first.
    if sys.platform == "win32":
        for root, sub in (
            ("HKCU", r"SOFTWARE\Valve\Steam"),
            ("HKLM", r"SOFTWARE\Valve\Steam"),
            ("HKLM", r"SOFTWARE\WOW6432Node\Valve\Steam"),
        ):
            found = _reg_steam_path(root, sub)
            if found:
                return found
    candidates: list[Path] = []
    if sys.platform == "win32":
        pf86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")
        pf = os.environ.get("ProgramFiles", r"C:\Program Files")
        candidates += [Path(pf86) / "Steam", Path(pf) / "Steam"]
    else:
        home = Path.home()
        candidates += [
            home / ".steam" / "steam",
            home / ".local" / "share" / "Steam",
            Path("/usr/share/steam"),
            home / ".var" / "app" / "com.valvesoftware.Steam" / ".local" / "share" / "Steam",
        ]
    for c in candidates:
        if (c / "userdata").is_dir():
            return c
    return None


def _reg_steam_path(root: str, subkey: str) -> Path | None:
    try:
        import winreg

        hive = winreg.HKEY_CURRENT_USER if root == "HKCU" else winreg.HKEY_LOCAL_MACHINE
        with winreg.OpenKey(hive, subkey) as k:
            for value in ("SteamPath", "InstallPath"):
                try:
                    v, _ = winreg.QueryValueEx(k, value)
                    p = Path(str(v))
                    if (p / "userdata").is_dir():
                        return p
                except OSError:
                    continue
    except Exception:
        pass
    return None


def userdata_dirs(steam_dir: Path) -> list[Path]:
    base = steam_dir / "userdata"
    if not base.is_dir():
        return []
    out = [d for d in base.iterdir() if d.is_dir() and d.name.isdigit()]
    return sorted(out)


def resolve_user_dir(steam_dir: Path, user_id: str | None = None) -> Path:
    """Pick `<steam>/userdata/<id>`. Most-recently-modified wins if ambiguous."""
    dirs = userdata_dirs(steam_dir)
    if not dirs:
        raise FileNotFoundError(f"No numeric userdata dirs under {steam_dir / 'userdata'}")
    if user_id:
        cand = steam_dir / "userdata" / user_id
        if not cand.is_dir():
            raise FileNotFoundError(f"User dir not found: {cand}")
        return cand
    if len(dirs) == 1:
        return dirs[0]

    def mtime(d: Path) -> float:
        sc = d / "config" / "shortcuts.vdf"
        try:
            return sc.stat().st_mtime
        except OSError:
            try:
                return d.stat().st_mtime
            except OSError:
                return 0.0

    return max(dirs, key=mtime)


def config_dir(user_dir: Path) -> Path:
    c = user_dir / "config"
    c.mkdir(parents=True, exist_ok=True)
    return c


def grid_dir(user_dir: Path) -> Path:
    g = config_dir(user_dir) / "grid"
    g.mkdir(parents=True, exist_ok=True)
    return g


def shortcuts_path(user_dir: Path) -> Path:
    return config_dir(user_dir) / "shortcuts.vdf"


def proton_path_hint(windows_path: str) -> str:
    """Best-effort Windows → Proton path hint for log messages on Linux."""
    if sys.platform == "win32" or not re.match(r"^[A-Za-z]:[\\/]", windows_path):
        return windows_path
    drive = windows_path[0].lower()
    rest = windows_path[2:].replace("\\", "/").lstrip("/")
    return f"~/.steam/steam/steamapps/compatdata/<appid>/pfx/drive_{drive}/{rest}"


STEAMID64_BASE = 76561197960265728


def account_id_from_steamid64(steamid64: int | str) -> str:
    """Map a 64-bit SteamID to the userdata dir name (accountID32)."""
    return str(int(steamid64) - STEAMID64_BASE)


def read_login_users(steam_dir: Path) -> list[dict[str, str]]:
    """Parse <steam>/config/loginusers.vdf → [{steamid64, account, persona, recent, ts}]."""
    path = steam_dir / "config" / "loginusers.vdf"
    if not path.is_file():
        return []
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    users: list[dict[str, str]] = []
    # Tiny text-VDF scan: "users" { "steamid64" { "AccountName" "x" ... } }
    for m in re.finditer(r'"(\d{17})"\s*\{(.*?)\n\s*\}', text, re.DOTALL):
        sid, body = m.group(1), m.group(2)
        def val(key: str) -> str:
            mm = re.search(rf'"{re.escape(key)}"\s+"(.*?)"', body)
            return mm.group(1) if mm else ""
        users.append({
            "steamid64": sid,
            "account_id": account_id_from_steamid64(sid),
            "account": val("AccountName"),
            "persona": val("PersonaName"),
            "recent": val("MostRecent"),
            "ts": val("Timestamp"),
        })
    return users


def most_recent_account_id(steam_dir: Path) -> str | None:
    """Best-effort current account: MostRecent flag, else highest login Timestamp."""
    users = read_login_users(steam_dir)
    if not users:
        return None
    flagged = [u for u in users if u["recent"] == "1"]
    if flagged:
        return flagged[0]["account_id"]
    def ts(u: dict[str, str]) -> int:
        try:
            return int(u["ts"])
        except ValueError:
            return 0
    return max(users, key=ts)["account_id"]


def resolve_all_user_dirs(steam_dir: Path, user_ids: list[str] | None = None) -> list[Path]:
    """All numeric userdata dirs (default), or only the requested --user-id values."""
    if user_ids:
        out: list[Path] = []
        for uid in user_ids:
            cand = steam_dir / "userdata" / uid
            if not cand.is_dir():
                raise FileNotFoundError(f"User dir not found: {cand}")
            out.append(cand)
        return out
    dirs = userdata_dirs(steam_dir)
    if not dirs:
        raise FileNotFoundError(f"No numeric userdata dirs under {steam_dir / 'userdata'}")
    return dirs

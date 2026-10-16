"""Xbox / UWP scanner — enumerates AppX packages via PowerShell.

Shortcut layout (brief conflated these into one field; split correctly):
    Exe           = C:\\Windows\\explorer.exe
    LaunchOptions = shell:AppsFolder\\<PackageFamilyName>!<AppId>

AppId comes from the package manifest's first <Application Id="...">;
falls back to `!App` when the manifest cannot be read.
Windows-only: returns [] elsewhere.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from ..models import GameInfo
from ..utils.win import RUN_KWARGS

EXPLORER = r"C:\Windows\explorer.exe"
SKIP_FAMILIES = (
    "Microsoft.Windows",
    "Microsoft.VCLibs",
    "Microsoft.NET",
    "Microsoft.UI",
    "Microsoft.AAD",
    "Microsoft.AccountsControl",
    "Microsoft.AsyncTextService",
    "Microsoft.BioEnrollment",
    "Microsoft.CredDialogHost",
    "Microsoft.LockApp",
    "Microsoft.MicrosoftEdge",
    "Microsoft.Win32WebViewHost",
    "Microsoft.XboxGameCallableUI",
    "Microsoft.Xbox.TCUI",
    "Microsoft.SecHealthUI",
    "Microsoft.ECApp",
    "Microsoft.DirectXRuntime",
    "Microsoft.Services.Store",
    "Microsoft.WinAppRuntime",
    # Media codec / image extensions (Store packages, not games).
    "Microsoft.AV1VideoExtension",
    "Microsoft.AVCEncoderVideoExtension",
    "Microsoft.HEVCVideoExtension",
    "Microsoft.MPEG2VideoExtension",
    "Microsoft.HEIFImageExtension",
    "Microsoft.VP9VideoExtensions",
    "Microsoft.RawImageExtension",
    "Microsoft.WebMediaExtensions",
    "Microsoft.WebpImageExtension",
    # Store / installer infrastructure and helpers.
    "Microsoft.DesktopAppInstaller",
    "Microsoft.AppInstaller",
    "Microsoft.Winget.Source",
    "Microsoft.StorePurchaseApp",
    "Microsoft.GetHelp",
    "Microsoft.Getstarted",
    "Microsoft.WindowsFeedbackHub",
    "Microsoft.Tips",
    # Built-in utilities.
    "Microsoft.MSPaint",
    "Microsoft.ScreenSketch",
    "Microsoft.People",
    "Microsoft.YourPhone",
    "Microsoft.MicrosoftStickyNotes",
    "Microsoft.549981C3F5F10",
    "MicrosoftCorporationII.",
    "MicrosoftWindows.",
    "Windows.",
    "windows.",
)
SKIP_EXACT = {"Microsoft.MicrosoftEdgeDevToolsClient", "ROBLOXCorporation.RobloxGDK"}
_GUID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

# Friendly display names for well-known packages (raw AppX names are ugly).
FRIENDLY_NAMES = {
    "Microsoft.MinecraftUWP": "Minecraft (Bedrock)",
    "Microsoft.4297127D64EC6": "Minecraft Launcher",
    "Microsoft.MinecraftEducationEdition": "Minecraft Education",
    "ROBLOXCORPORATION.ROBLOX": "Roblox",
    "Microsoft.GamingApp": "Xbox",
}


def friendly_name(name: str) -> str:
    if name in FRIENDLY_NAMES:
        return FRIENDLY_NAMES[name]
    # "PocketpairInc.Palworld" -> "Palworld" (strip vendor prefix).
    if "." in name and " " not in name:
        tail = name.split(".")[-1]
        if tail and tail[0].isupper():
            return tail
    return name


def _should_skip(family: str, name: str) -> bool:
    if not name.strip() or _GUID_RE.match(name.strip()):
        return True
    if name.strip() in SKIP_EXACT:
        return True
    return any(family.startswith(p) for p in SKIP_FAMILIES) or not family.strip()


# Manifest extension categories that mark a package as an app/utility rather than
# a game (verified against installed packages: Paint, Notepad, Calculator, NanaZip,
# Xbox app, Game Bar, Store, and codec packages all declare at least one of these;
# Minecraft and Roblox declare only protocol/file-association entries).
UTILITY_EXTENSIONS = {
    "appexecutionalias", "appextension", "appextensionhost", "comserver",
    "fileexplorercontextmenus", "desktopappmigration", "service", "backgroundtasks",
    "startuptask", "appservice", "fulltrustprocess", "sharetarget",
}
# Substrings of package names that are OS/driver/runtime components, not games.
SYSTEM_NAME_TOKENS = (
    "extension", "dialog", "picker", "webviewhost", "experiencehost", "oobe",
    "captiveportal", "nvidiacorp.", "xboxgamecallable", "xbox.tcui", "xboxidentity",
    "accountscontrol", "lockapp", "credential", "bioenrollment", "appcompat",
    "winappruntime", "appinstaller", "winget", "devkit", "narratorquickstart",
    "applicationcompatibility",
    "asynctextservice", "chxapp", "cloudexperience", "contentdelivery",
    "startmenu", "shellexperience", "windows.", "microsoftwindows.",
)


def _extension_kinds(raw: str) -> set[str]:
    kinds: set[str] = set()
    for item in raw.split(","):
        item = item.strip()
        if item:
            kinds.add(item.removeprefix("windows.").lower())
    return kinds


def _is_utility(pkg: dict) -> bool:
    name = str(pkg.get("Name", "")).strip()
    lowered = name.lower()
    if any(tok in lowered for tok in SYSTEM_NAME_TOKENS):
        return True
    if pkg.get("Manifest"):
        kinds = _extension_kinds(str(pkg.get("Exts", "")))
        if kinds & UTILITY_EXTENSIONS:
            return True
    return False


def _manifest_app_id(install: str) -> str | None:
    m = Path(install) / "AppxManifest.xml"
    if not m.is_file():
        return None
    try:
        root = ET.parse(m).getroot()
        for el in root.iter():
            if el.tag.endswith("Application") and el.get("Id"):
                return str(el.get("Id"))
    except ET.ParseError:
        return None
    return None


def _manifest_art(install: str) -> dict[str, str]:
    """Logo from AppxManifest.xml VisualElements, if the file is readable.

    Manifests name scaled assets without their scale suffix (Logo.png for
    Logo.scale-200.png), so the largest existing scale is used.
    """
    base = Path(install)
    try:
        root = ET.parse(base / "AppxManifest.xml").getroot()
    except (ET.ParseError, OSError):
        return {}
    for el in root.iter():
        if not el.tag.endswith("VisualElements"):
            continue
        for key in ("Square310x310Logo", "Square150x150Logo"):
            rel = (el.get(key) or "").strip()
            if not rel:
                continue
            exact = base / rel
            if exact.is_file():
                return {"squares": str(exact)}
            scaled = sorted(exact.parent.glob(f"{Path(rel).stem}.scale-*{Path(rel).suffix}"))
            if scaled:
                return {"squares": str(scaled[-1])}
    return {}


def _query_packages() -> list[dict]:
    # Application Ids come from the manifest API (Get-AppxPackageManifest):
    # C:\Program Files\WindowsApps is ACL-locked, so reading AppxManifest.xml
    # directly fails and the old "!App" fallback produced dead shortcuts
    # (e.g. Minecraft Bedrock needs "!Game").
    ps = (
        "Get-AppxPackage | ForEach-Object {"
        " $m = $null; try { $m = Get-AppxPackageManifest -Package $_.PackageFullName } catch {};"
        " $ids = @(); $exts = @(); if ($m) {"
        " $ids = @($m.Package.Applications.Application | ForEach-Object { $_.Id });"
        " $exts = @($m.Package.Applications.Application.Extensions.Extension | ForEach-Object { $_.Category } | Where-Object { $_ }) };"
        " [PSCustomObject]@{ Name=$_.Name; PackageFamilyName=$_.PackageFamilyName;"
        " PackageFullName=$_.PackageFullName; InstallLocation=$_.InstallLocation;"
        " AppIds=($ids -join ','); Exts=(($exts | Select-Object -Unique) -join ','); Manifest=[bool]$m } } | ConvertTo-Json -Compress"
    )
    try:
        proc = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps],
            capture_output=True,
            text=True,
            timeout=120,
            **RUN_KWARGS,
        )
    except (OSError, subprocess.SubprocessError):
        return []
    if proc.returncode != 0 or not proc.stdout.strip():
        return []
    try:
        data = json.loads(proc.stdout)
    except json.JSONDecodeError:
        return []
    if isinstance(data, dict):
        return [data]
    return data if isinstance(data, list) else []


class XboxScanner:
    name = "xbox"

    def scan(self) -> list[GameInfo]:
        if sys.platform != "win32":
            return []
        if os.environ.get("SGM_SKIP_XBOX") == "1":
            return []
        from .gaming_root import gaming_root_art, scan_gaming_roots

        out: list[GameInfo] = []
        package_names: set[str] = set()
        root_art = gaming_root_art()
        for pkg in _query_packages():
            try:
                family = str(pkg.get("PackageFamilyName", "")).strip()
                name = str(pkg.get("Name", "")).strip()
                install = str(pkg.get("InstallLocation", "")).strip()
                full = str(pkg.get("PackageFullName", "")).strip()
                if not family or _should_skip(family, name) or _is_utility(pkg):
                    continue
                app_ids = [a for a in str(pkg.get("AppIds", "")).split(",") if a.strip()]
                if not app_ids:
                    app_ids = [(_manifest_app_id(install) or "App")]
                art = dict(root_art.get(name.lower()) or _manifest_art(install))
                for app_id in app_ids:
                    label = friendly_name(name)
                    if len(app_ids) > 1:
                        label = f"{label} ({app_id})"
                    out.append(
                        GameInfo(
                            name=label,
                            exe=EXPLORER,
                            start_dir=r"C:\Windows",
                            launch_options=f"shell:AppsFolder\\{family}!{app_id}",
                            source="xbox",
                            source_id=full or family,
                            tags=["Xbox", "UWP"],
                            art_urls=dict(art),
                        )
                    )
                    package_names.add(name.lower())
            except Exception:
                continue

        seen = {(g.name.lower(), g.exe.lower()) for g in out}
        for game in scan_gaming_roots(skip_names=package_names):
            if (game.name.lower(), game.exe.lower()) not in seen:
                out.append(game)
        return out

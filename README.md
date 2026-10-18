# Steam Games Manager

Bring your Epic, GOG, Xbox, Ubisoft, EA, and Battle.net games into Steam as
non-Steam shortcuts with proper cover art, banners, logos, and icons, no
SteamGridDB key required.

<p align="center">
    <a href="https://github.com/xHybred/SteamGamesMananger/releases">
        <img alt="GitHub Release" src="https://img.shields.io/github/v/release/xHybred/SteamGamesMananger?label=release">
    </a>
</p>

![Library view](docs/images/librarylist.png)

![Library view](docs/images/librarygrid.png)

## What it does

- Scans every major PC launcher on your machine and lists the games it finds.
- Adds any game to Steam's `shortcuts.vdf` with one click (or one command),
  backed up before every write.
- Fetches artwork automatically: each launcher's own cached art first,
  then keyless catalog lookups, with SteamGridDB as an optional upgrade.
- Remembers what's already imported.
- Some games require extra services or launchers to start otherwise authentication fails. SGM applies advanced shortcuts so games that traditional don't work run.

## Supported launchers

| Launcher | Detection | Artwork source |
|---|---|---|
| Epic Games Store | `Manifests/*.item` + `catcache.bin` | Catalog cache, then catalog search |
| GOG Galaxy | Registry + `galaxy-2.0.db` | Galaxy webcache covers, then catalog |
| Xbox / Game Pass | AppX packages + `.GamingRoot` | `MicrosoftGame.config` images, manifest logos |
| Ubisoft Connect | Registry + local configurations cache | Ubisoft CDN thumbnails and logos |
| EA App | Uninstall records + bundled title list | Title list, then the game's exe icon |
| Battle.net | Agent `product.db` + `aggregate.json` | Agent logos, then hosted covers |
| Desktop | Win32 games outside launcher manifests (Roblox Studio) | Online lookup |

## Artwork

Four kinds per game, matching Steam's grid filenames: cover (`<appid>p.jpg`,
600x900), hero (`<appid>_hero.jpg`), logo (`<appid>_logo.png`), and icon
(`<appid>.ico`). Local store art always wins over online results. Square-only
logos (Minecraft, Roblox) are composed over a blurred splash backdrop instead
of being stretched.

## Architecture

- **Desktop Framework:** WPF
- **UI Markup:** XAML
- **UI Library:** [WPF UI](https://github.com/lepoco/wpfui)
- **Language:** C#
- **Runtime:** .NET (modern, `net10.0-windows`)

## Quick start

Download `SGM.zip` from Releases, close Steam, and run it.
Pick your launchers at the bottom, click **+** on a game (or **Add All New**),
then restart Steam.

## CLI, for scripting

The command-line front end shares the same core as the GUI:

```powershell
# Scan only, change nothing
sgm --dry-run

# Full import (prompts for a SteamGridDB key only if you want it)
sgm --api-key SGDB_KEY --launchers epic,gog,xbox

# Non-interactive, specific user
sgm --user-id 12345678 --force
```

| Option | Effect |
|---|---|
| `--launchers` | Comma list: `epic,gog,xbox,ubisoft,ea,battlenet,desktop` |
| `--steam-dir DIR` | Steam install directory (auto-detected otherwise) |
| `--user-id ID` | Target account id (repeatable). Default: all accounts |
| `--api-key KEY` | SteamGridDB API key (or `$STEAMGRIDDB_API_KEY`) |
| `--dry-run` | Scan and report without writing |
| `--no-artwork` | Skip all artwork downloads |
| `--overwrite` | Update already-imported shortcuts |
| `--force` | Write even if Steam is running (risky) |
| `--prune` | Remove leftover system-junk entries |
| `--include TEXT` | Only import games whose name contains `TEXT` |
| `--epic-manifests DIR` | Override the Epic `Manifests` directory |
| `--gog-db PATH` | Override the `galaxy-2.0.db` path |
| `--gui` | Launch the graphical interface |
| `-h, --help` | Show help |

Steam must be closed before writing. The tool aborts if `steam.exe` is
running unless `--force` is passed.

## Project layout

```
Sgm.App/          # WPF UI (WPF UI library, MVVM via CommunityToolkit.Mvvm)
Sgm.Cli/          # Command-line entry point
Sgm.Core/         # Scanners, artwork clients, vdf codec, settings
Sgm.Core.Tests/   # xUnit tests
Sgm.slnx          # Solution
```

## Building

Requires the .NET SDK (matching the `net10.0-windows` target).

```powershell
# Build everything
dotnet build Sgm.slnx -c Release

# Run the tests
dotnet test Sgm.slnx -c Release
```

Outputs:

- GUI: `Sgm.App\bin\Release\net10.0-windows\SteamGamesManager.exe`
- CLI: `Sgm.Cli\bin\Release\net10.0-windows\sgm.exe`

## License note

Ubisoft record parsing, the Battle.net protobuf schema, and the EA title list
are derived from [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper)
(GPL-3.0). Check license compatibility before distributing builds.

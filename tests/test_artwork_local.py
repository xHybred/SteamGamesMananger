import json
import sqlite3

from PIL import Image

from steam_games_manager.artwork import auto
from steam_games_manager.scanners import battlenet, gaming_root, gog, launchers
from steam_games_manager.scanners import xbox


def _png(path, size=(60, 40)):
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.new("RGB", size, (200, 30, 30)).save(path, "PNG")
    return path


def _galaxy_db(root):
    db = root / "GOG.com" / "Galaxy" / "storage" / "galaxy-2.0.db"
    db.parent.mkdir(parents=True)
    con = sqlite3.connect(db)
    con.executescript(
        "CREATE TABLE WebCacheResourceTypes (id INTEGER, type TEXT);"
        "CREATE TABLE WebCache (id INTEGER, releaseKey TEXT, userId INTEGER);"
        "CREATE TABLE WebCacheResources (webCacheId INTEGER, webCacheResourceTypeId INTEGER, filename TEXT);"
        "CREATE TABLE GamePieceTypes (id INTEGER, type TEXT);"
        "CREATE TABLE GamePieces (releaseKey TEXT, gamePieceTypeId INTEGER, userId INTEGER, value TEXT);"
        "INSERT INTO WebCacheResourceTypes VALUES (3, 'verticalCover');"
        "INSERT INTO WebCache VALUES (141, 'gog_111', 999);"
        "INSERT INTO WebCacheResources VALUES (141, 3, 'cover.webp');"
        "INSERT INTO WebCache VALUES (142, 'gog_222', 999);"
        "INSERT INTO WebCacheResources VALUES (142, 3, 'missing.webp');"
        "INSERT INTO GamePieceTypes VALUES (378, 'originalImages');"
        "INSERT INTO GamePieces VALUES ('gog_333', 378, 999, "
        "'{\"verticalCover\": \"https://images.gog.com/v.webp\"}');"
    )
    con.commit()
    con.close()
    _png(root / "GOG.com" / "Galaxy" / "webcache" / "999" / "gog" / "111" / "cover.webp")
    return db


def test_galaxy_art_prefers_local_webcache_then_gamepiece_url(tmp_path):
    art = gog._galaxy_art(_galaxy_db(tmp_path))
    assert art["111"]["grids"].endswith("cover.webp")
    assert "222" not in art
    assert art["333"] == {"grids": "https://images.gog.com/v.webp"}


def test_download_accepts_local_file(tmp_path):
    img = auto._download(str(_png(tmp_path / "a.png", (30, 50))))
    assert img is not None and img.size == (30, 50)


def test_gaming_root_config_art_resolves_game_folder(tmp_path):
    game = tmp_path / "Shadow"
    content = game / "Content"
    (content / "Assets").mkdir(parents=True)
    _png(content / "Assets" / "Splash.png")
    config = content / "MicrosoftGame.config"
    config.write_text(
        '<Game configVersion="1"><Identity Name="Some.Shadow"/>'
        '<ShellVisuals DefaultDisplayName="Shadow" SplashScreenImage="Assets\\Splash.png"/>'
        '</Game>', encoding="utf-8")
    art = gaming_root._config_art(config)
    assert art["grids"].endswith("Splash.png") and art["heroes"].endswith("Splash.png")


def test_appx_manifest_art_uses_largest_scale(tmp_path):
    install = tmp_path / "pkg"
    (install / "Assets").mkdir(parents=True)
    _png(install / "Assets" / "Logo.scale-100.png")
    _png(install / "Assets" / "Logo.scale-200.png")
    (install / "AppxManifest.xml").write_text(
        '<Package><Applications><Application><VisualElements '
        'Square150x150Logo="Assets\\Logo.png"/></Application></Applications></Package>',
        encoding="utf-8")
    art = xbox._manifest_art(str(install))
    assert art["squares"].endswith("Logo.scale-200.png")


def test_battlenet_aggregate_logo_and_hosted_fallback(tmp_path):
    (tmp_path / "aggregate.json").write_text(json.dumps({"installed": [
        {"product_id": "S2", "logo_art_uri": "https://cdn.example/s2.jpg"}]}), encoding="utf-8")
    logos = battlenet.aggregate_logos(tmp_path)
    assert logos == {"S2": "https://cdn.example/s2.jpg"}
    assert battlenet.HOSTED_COVER.format(uid="s2").endswith("/battlenet/s2.webp")


def test_ea_title_match_exact_fuzzy_and_none():
    assert launchers._ea_catalog(), "bundled ea_app_titles.json must load"
    exact = launchers._ea_art("2006 FIFA World Cup")
    assert exact.get("grids", "").startswith("https://")
    assert launchers._ea_art("Zzqx Nonexistent Title 987") == {}

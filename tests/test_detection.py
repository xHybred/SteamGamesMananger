import base64
import json

from steam_games_manager import main as sgm_main
from steam_games_manager.models import GameInfo
from steam_games_manager.scanners import epic, gaming_root, launchers


def test_epic_category_filter_keeps_only_games(tmp_path):
    base = {"DisplayName": "X", "AppName": "X", "InstallLocation": "C:\\x"}
    game = tmp_path / "game.item"
    game.write_text(json.dumps({**base, "AppCategories": ["public", "games", "applications"]}))
    engine = tmp_path / "engine.item"
    engine.write_text(json.dumps({**base, "AppCategories": ["engines/ue5", "engines"]}))
    hidden = tmp_path / "hidden.item"
    hidden.write_text(json.dumps({**base, "AppCategories": ["Content", "hidden"]}))
    assert epic.parse_manifest(game, "launcher.exe") is not None
    assert epic.parse_manifest(engine, "launcher.exe") is None
    assert epic.parse_manifest(hidden, "launcher.exe") is None


def test_epic_catalog_art_is_exact_and_attached(tmp_path):
    items = [{"id": "abc", "keyImages": [
        {"type": "DieselGameBoxTall", "url": "https://cdn/tall.jpg"},
        {"type": "DieselGameBox", "url": "https://cdn/box.jpg"},
    ]}, {"id": "nothing", "keyImages": []}]
    cache = tmp_path / "catcache.bin"
    cache.write_bytes(base64.b64encode(json.dumps(items).encode("utf-8")))
    catalog = epic.load_catalog_art(cache)
    assert catalog["abc"] == {"grids": "https://cdn/tall.jpg", "heroes": "https://cdn/box.jpg"}
    assert "nothing" not in catalog
    manifest = tmp_path / "g.item"
    manifest.write_text(json.dumps({"DisplayName": "Game", "AppName": "G",
                                    "InstallLocation": "C:\\g", "CatalogItemId": "abc",
                                    "AppCategories": ["games"]}))
    game = epic.parse_manifest(manifest, "launcher.exe", catalog)
    assert game is not None and game.art_urls["grids"] == "https://cdn/tall.jpg"


def test_epic_catalog_missing_file_is_empty(tmp_path):
    assert epic.load_catalog_art(tmp_path / "nope.bin") == {}


def test_gaming_root_config_parse(tmp_path):
    content = tmp_path / "Shadow" / "Content"
    content.mkdir(parents=True)
    (content / "Shadow.exe").write_bytes(b"MZ")
    config = content / "MicrosoftGame.config"
    config.write_text(
        '<?xml version="1.0"?>'
        '<Game configVersion="1">'
        '<Identity Name="Some.Shadow" Publisher="CN=X"/>'
        '<ShellVisuals DefaultDisplayName="Shadow Quest" />'
        '<ExecutableList><Executable Name="Shadow.exe" Id="Shadow"/></ExecutableList>'
        '</Game>', encoding="utf-8")
    parsed = gaming_root._parse_game_config(config)
    assert parsed == ("Shadow Quest", str(content / "Shadow.exe"))


def test_gaming_root_skips_missing_executable(tmp_path):
    content = tmp_path / "Ghost" / "Content"
    content.mkdir(parents=True)
    config = content / "MicrosoftGame.config"
    config.write_text('<Game configVersion="1"><ShellVisuals DefaultDisplayName="G"/>'
                      '<ExecutableList><Executable Name="missing.exe"/></ExecutableList></Game>',
                      encoding="utf-8")
    assert gaming_root._parse_game_config(config) is None


def test_main_exe_prefers_largest_non_helper(tmp_path):
    (tmp_path / "Game.exe").write_bytes(b"x" * 5000)
    (tmp_path / "GameHelper.exe").write_bytes(b"x" * 9000)
    (tmp_path / "unins000.exe").write_bytes(b"x" * 9999)
    (tmp_path / "Crash_Reporter.exe").write_bytes(b"x" * 9999)
    assert launchers.pick_main_exe(tmp_path).name == "Game.exe"


def test_main_exe_none_when_only_helpers(tmp_path):
    (tmp_path / "setup.exe").write_bytes(b"x")
    assert launchers.pick_main_exe(tmp_path) is None


def test_cross_store_duplicates_keep_highest_priority():
    games = [
        GameInfo(name="Palworld", exe="a.exe", source="epic"),
        GameInfo(name="Palworld!", exe="b.exe", source="xbox"),
        GameInfo(name="Other", exe="c.exe", source="gog"),
    ]
    out = sgm_main.dedupe_titles(games)
    assert [g.source for g in out] == ["epic", "gog"]

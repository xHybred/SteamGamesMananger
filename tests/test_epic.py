import json


def test_epic_manifest_parsing(tmp_path):
    from steam_games_manager.scanners.epic import parse_manifest

    manifest = {
        "DisplayName": "Demo Game",
        "AppName": "DemoApp",
        "CatalogNamespace": "demo",
        "InstallLocation": "C:\\Games\\Demo",
        "LaunchExecutable": "Demo.exe",
        "bIsApplication": True,
    }
    p = tmp_path / "Demo.item"
    p.write_text(json.dumps(manifest), encoding="utf-8")
    g = parse_manifest(p, launcher="C:\\Epic\\EpicGamesLauncher.exe")
    assert g is not None
    assert g.name == "Demo Game"
    assert g.exe.endswith("EpicGamesLauncher.exe")
    # No leading dash before protocol (brief had an erroneous '-').
    assert g.launch_options == "com.epicgames.launcher://apps/DemoApp?action=launch&silent=true"


def test_epic_manifest_skips_non_games(tmp_path):
    from steam_games_manager.scanners.epic import parse_manifest

    p = tmp_path / "ue.item"
    p.write_text(json.dumps({"DisplayName": "UE", "AppName": "UE", "CatalogNamespace": "ue"}), encoding="utf-8")
    assert parse_manifest(p, launcher="x") is None


def test_merge_dedupes_without_overwrite():
    from steam_games_manager.models import GameInfo
    from steam_games_manager.vdf_handler import shortcuts as sc

    data = {"shortcuts": {}}
    g = GameInfo(name="A", exe="C:\\x.exe", start_dir="C:\\", launch_options="", source="epic")
    data, s1 = sc.merge_games(data, [g])
    assert s1["added"] == 1
    data, s2 = sc.merge_games(data, [g])
    assert s2["skipped"] == 1 and len(data["shortcuts"]) == 1
    data, s3 = sc.merge_games(data, [g], overwrite=True)
    assert s3["updated"] == 1

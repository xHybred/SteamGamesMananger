from steam_games_manager.models import GameInfo
from steam_games_manager.scanners import xbox as xbox_mod
from steam_games_manager.vdf_handler import shortcuts as sc


def test_xbox_skips_guid_and_system_names():
    assert xbox_mod._should_skip("XYZ", "1527c705-839a-4832-9118-54d4Bd6a0c89")
    assert xbox_mod._should_skip("Microsoft.LockApp_abc", "Microsoft.LockApp")
    assert xbox_mod._should_skip("MicrosoftWindows.Client.CBS_x", "MicrosoftWindows.Client.CBS")
    assert xbox_mod._should_skip("Windows.PrintDialog_x", "Windows.PrintDialog")
    assert not xbox_mod._should_skip("PocketpairInc.Palworld_x", "PocketpairInc.Palworld")


def test_prune_removes_only_system_entries():
    data = {"shortcuts": {
        "0": {"AppName": "Dead Island 2", "Exe": '"C:\\x.exe"'},
        "1": {"AppName": "Microsoft.LockApp", "Exe": '"C:\\Windows\\explorer.exe"'},
        "2": {"AppName": "1527c705-839a-4832-9118-54d4Bd6a0c89", "Exe": '"C:\\Windows\\explorer.exe"'},
    }}
    data, removed = sc.prune_system_entries(data)
    assert removed == 2
    assert list(data["shortcuts"]) == ["0"]


def test_write_account_and_prune_roundtrip(tmp_path):
    from steam_games_manager.main import write_account

    user_dir = tmp_path / "userdata" / "999"
    g = GameInfo(name="Demo", exe="C:\\demo.exe", start_dir="C:\\", source="epic")
    res = write_account(user_dir, [g])
    assert res["error"] is None and res["added"] == 1
    res2 = write_account(user_dir, [g])
    assert res2["skipped"] == 1

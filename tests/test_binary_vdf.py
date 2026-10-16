def test_binary_vdf_roundtrip():
    from steam_games_manager.vdf_handler import binary_vdf

    data = {
        "shortcuts": {
            "0": {
                "appid": -1392444581,
                "AppName": "Foo",
                "Exe": '"C:\\Games\\Foo\\foo.exe"',
                "StartDir": '"C:\\Games\\Foo"',
                "icon": "",
                "ShortcutPath": "",
                "LaunchOptions": "com.epicgames.launcher://apps/Foo?action=launch&silent=true",
                "IsHidden": 0,
                "AllowDesktopConfig": 1,
                "AllowOverlay": 1,
                "OpenVR": 0,
                "LastPlayTime": 0,
                "tags": {"0": "Epic"},
            }
        }
    }
    assert binary_vdf.loads(binary_vdf.dumps(data)) == data


def test_binary_vdf_empty_and_corrupt():
    import pytest

    from steam_games_manager.vdf_handler import binary_vdf

    assert binary_vdf.loads(b"") == {}
    with pytest.raises(ValueError):
        binary_vdf.loads(b"\x01key\x00unterminated")

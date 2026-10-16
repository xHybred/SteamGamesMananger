from steam_games_manager import config as cfg


def test_account_id_mapping_known_values():
    assert cfg.account_id_from_steamid64(76561198370275900) == "410010172"
    assert cfg.account_id_from_steamid64(76561199133034005) == "1172768277"
    assert cfg.account_id_from_steamid64("76561199112656960") == "1152391232"


def test_loginusers_parsing_and_recent(tmp_path):
    steam = tmp_path / "Steam"
    (steam / "config").mkdir(parents=True)
    (steam / "config" / "loginusers.vdf").write_text(
        '"users"\n{\n'
        '\t"76561198370275900"\n\t{\n'
        '\t\t"AccountName"\t\t"kingownagexv"\n'
        '\t\t"PersonaName"\t\t"Hybred"\n'
        '\t\t"Timestamp"\t\t"1792051172"\n'
        '\t}\n'
        '\t"76561199133034005"\n\t{\n'
        '\t\t"AccountName"\t\t"thyhybred"\n'
        '\t\t"PersonaName"\t\t"xHybred"\n'
        '\t\t"Timestamp"\t\t"1791679260"\n'
        '\t}\n}\n',
        encoding="utf-8",
    )
    users = cfg.read_login_users(steam)
    assert {u["account_id"] for u in users} == {"410010172", "1172768277"}
    assert cfg.most_recent_account_id(steam) == "410010172"


def test_resolve_all_user_dirs(tmp_path):
    steam = tmp_path / "Steam"
    (steam / "userdata" / "111" / "config").mkdir(parents=True)
    (steam / "userdata" / "222" / "config").mkdir(parents=True)
    assert [d.name for d in cfg.resolve_all_user_dirs(steam)] == ["111", "222"]
    assert [d.name for d in cfg.resolve_all_user_dirs(steam, ["222"])] == ["222"]

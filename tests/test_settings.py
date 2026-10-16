from steam_games_manager import settings


def test_defaults_xbox_off():
    d = settings.defaults()
    assert d["sources"] == {"epic": True, "gog": True, "xbox": False,
                            "ubisoft": True, "ea": True, "battlenet": True, "desktop": True}
    assert d["overwrite"] is True
    assert d["view"] == "list"
    assert d["density"] == "compact"


def test_roundtrip_and_hardening(tmp_path):
    p = tmp_path / "settings.json"
    state = settings.defaults()
    state["sources"]["epic"] = False
    state["density"] = "normal"
    state["api_key"] = "abc"
    state["hidden"] = [["xbox", "VP9", "exe"]]
    settings.save(state, p)
    back = settings.load(p)
    assert back["sources"]["epic"] is False
    assert back["sources"]["xbox"] is False
    assert back["density"] == "normal"
    assert back["api_key"] == "abc"
    assert back["hidden"] == [["xbox", "VP9", "exe"]]


def test_load_missing_and_corrupt(tmp_path):
    assert settings.load(tmp_path / "nope.json")["sources"]["gog"] is True
    bad = tmp_path / "bad.json"
    bad.write_text("{not json", encoding="utf-8")
    loaded = settings.load(bad)
    assert loaded["view"] == "list" and loaded["hidden"] == []
    weird = tmp_path / "weird.json"
    weird.write_text('{"view": "-coverflow-", "hidden": [["a"]]}', encoding="utf-8")
    assert settings.load(weird)["view"] == "list"

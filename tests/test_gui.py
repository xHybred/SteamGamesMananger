from steam_games_manager import gui
from steam_games_manager.models import GameInfo


def _game(name="Demo", exe="C:\\demo.exe"):
    return GameInfo(name=name, exe=exe, start_dir="C:\\", source="epic")


def test_in_steam_set_and_status():
    data = {"shortcuts": {"0": {"AppName": "Demo", "Exe": '"C:\\demo.exe"'}}}
    states = [gui.in_steam_set(data)]
    assert gui.is_in_steam(_game(), states) is True
    assert gui.is_in_steam(_game("Other"), states) is False
    assert gui.is_in_steam(_game(), []) is False


def test_summary_counts():
    games = [_game("A"), _game("B")]
    in_steam = {gui.gkey(games[0])}
    assert gui.summary_counts(games, in_steam) == (2, 1)
    assert gui.summary_counts([], set()) == (0, 0)


def test_visible_games_and_counts():
    from steam_games_manager.models import GameInfo
    games = [GameInfo(name="A", exe="a", source="epic"),
             GameInfo(name="B", exe="b", source="xbox"),
             GameInfo(name="C", exe="c", source="gog")]
    all_on = {"epic": True, "gog": True, "xbox": True, "desktop": True}
    assert len(gui.visible_games(games, all_on, "")) == 3
    assert [g.name for g in gui.visible_games(games, {**all_on, "xbox": False}, "")] == ["A", "C"]
    assert [g.name for g in gui.visible_games(games, all_on, "b")] == ["B"]
    assert gui.source_counts(games) == {"epic": 1, "xbox": 1, "gog": 1}


def test_friendly_names():
    from steam_games_manager.scanners.xbox import friendly_name
    assert friendly_name("Microsoft.MinecraftUWP") == "Minecraft (Bedrock)"
    assert friendly_name("PocketpairInc.Palworld") == "Palworld"
    assert friendly_name("Plain Name") == "Plain Name"


def test_icons_render_and_cache():
    for name in ("plus", "picture", "hide", "grid", "user", "gear", "help", "dot"):
        img = gui.icon(name, "#c7d5e0", 16)
        assert img is gui.icon(name, "#c7d5e0", 16)  # cached
        assert img._light_image.size == (16, 16)
    assert gui.SOURCE_LABELS["epic"] == "Epic" and gui.SOURCE_LABELS["ubisoft"] == "Ubisoft"


def test_hover_handlers_change_and_restore_color():
    import queue

    app = gui.App(queue.Queue())
    try:
        app.games = [GameInfo(name="Hover", exe="C:\\h.exe", source="gog")]
        app._render_games()
        row = app.game_box.winfo_children()[0]
        base = row.cget("fg_color")
        assert row._hover_set is not None and row._hover_clear is not None
        import customtkinter as ctk

        add_btn = row.winfo_children()[-1].winfo_children()[0]
        assert isinstance(add_btn, ctk.CTkButton)
        before = add_btn.cget("fg_color")
        row._hover_set()
        assert row.cget("fg_color") != base
        assert add_btn.cget("fg_color") == before
        row._hover_clear()
        assert row.cget("fg_color") == base
        assert add_btn.cget("fg_color") == before
    finally:
        app.destroy()


def test_asset_path_finds_icon():
    p = gui.asset_path("sgm.ico")
    assert p is not None and p.is_file()


def test_app_builds_complete_ui():
    """Regression: every control the app touches must exist (no dead code)."""
    import queue

    app = gui.App(queue.Queue())
    try:
        for attr in ("status_var", "chip_btns", "view_group",
                     "density_group", "search_var", "list_canvas", "game_box", "summary_var",
                     "add_all_btn", "account_vars", "views", "nav_btns"):
            assert hasattr(app, attr), f"missing UI attribute: {attr}"
        assert set(app.chip_btns) == {"epic", "gog", "xbox", "ubisoft", "ea", "battlenet", "desktop"}
        assert app.density.get() in ("compact", "normal")
        assert set(app.view_group) == {"list", "grid"}
        assert set(app.density_group) == {"compact", "normal"}
        for parts in app.nav_btns.values():
            assert set(parts) == {"item", "edge", "btn", "glyph"}
    finally:
        app.destroy()

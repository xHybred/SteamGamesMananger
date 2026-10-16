import queue

from PIL import Image

from steam_games_manager import gui
from steam_games_manager.artwork import auto
from steam_games_manager.scanners import launchers


def test_ubisoft_logo_field_is_direct_or_localized():
    blob = ("thumb_image: l2\n"
            "logo_image: ff2a.png\n"
            "localizations:\n"
            "  default:\n"
            "    l2: a1f1.png\n")
    assert launchers._ubisoft_asset(blob, "logo_image") == launchers.UBISOFT_ASSETS + "ff2a.png"
    assert launchers._ubisoft_asset(blob, "thumb_image") == launchers.UBISOFT_ASSETS + "a1f1.png"
    assert launchers._ubisoft_asset(blob, "splash_image") == ""


def test_square_logo_cover_is_portrait_with_logo_centered(tmp_path):
    square = tmp_path / "logo.png"
    Image.new("RGBA", (480, 480), (255, 255, 255, 255)).save(square)
    splash = tmp_path / "splash.png"
    Image.new("RGB", (1920, 1080), (120, 30, 30)).save(splash)
    cover = auto.local_thumb_image({"squares": str(square), "heroes": str(splash)})
    assert cover is not None and cover.size == auto.GRID_SIZE
    cx, cy = cover.size[0] // 2, cover.size[1] // 2
    assert cover.getpixel((cx, cy)) == (255, 255, 255)
    assert cover.getpixel((5, 5)) != (255, 255, 255)


def test_gaming_root_prefers_square_logo_over_splash(tmp_path):
    from steam_games_manager.scanners import gaming_root
    content = tmp_path / "Content"
    content.mkdir()
    for name in ("LargeLogo.png", "SplashScreen.png"):
        Image.new("RGB", (8, 8)).save(content / name)
    config = content / "MicrosoftGame.config"
    config.write_text(
        '<Game><Identity Name="X"/><ShellVisuals SplashScreenImage="SplashScreen.png" '
        'Square480x480Logo="LargeLogo.png"/></Game>', encoding="utf-8")
    art = gaming_root._config_art(config)
    assert art["squares"].endswith("LargeLogo.png")
    assert art["heroes"].endswith("SplashScreen.png")
    assert "grids" not in art


def test_trim_transparent_crops_to_visible_pixels():
    img = Image.new("RGBA", (100, 50), (0, 0, 0, 0))
    img.paste((255, 255, 255, 255), (60, 10, 80, 30))
    assert auto._trim_transparent(img).size == (20, 20)


def test_mix_hex_blends_endpoints_and_midpoint():
    assert gui._mix_hex("#000000", "#ffffff", 0) == "#000000"
    assert gui._mix_hex("#000000", "#ffffff", 1) == "#ffffff"
    assert gui._mix_hex("#000000", "#ffffff", 0.5) == "#808080"


def test_flash_note_shows_then_hides():
    app = gui.App(queue.Queue())
    try:
        app.flash("Added.", hold=0, steps=2)
        assert app.flash_lbl.cget("text") == "Added."
        assert app.flash_lbl.place_info().get("anchor") == "w"
        app.update()
        app.update()
        app.update()
        assert not app.flash_lbl.winfo_ismapped()
        app.flash("Close Steam first.", gui.BAD, hold=5000, steps=2)
        assert app.flash_lbl.cget("text_color") == gui.BAD
    finally:
        app.destroy()


def test_add_all_button_keeps_dark_text_when_disabled():
    app = gui.App(queue.Queue())
    try:
        app.add_all_btn.configure(state="disabled")
        assert app.add_all_btn.cget("text_color_disabled") == gui.ACCENT_INK
        assert app.add_all_btn.cget("text_color") == gui.ACCENT_INK
    finally:
        app.destroy()

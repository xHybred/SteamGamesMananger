"""Offline tests for keyless artwork (no network)."""
from PIL import Image

from steam_games_manager.artwork import auto


def _img(w=1200, h=900, color=(200, 30, 30)):
    return Image.new("RGB", (w, h), color)


def test_first_url_type_precedence_and_video_skip():
    kis = [
        {"type": "heroCarouselVideo", "url": "http://x/vid"},
        {"type": "OfferImageWide", "url": "http://x/wide"},
        {"type": "Thumbnail", "url": "http://x/thumb"},
    ]
    assert auto._first_url(kis, auto.EPIC_HERO_TYPES) == "http://x/wide"
    assert auto._first_url(kis, auto.EPIC_ICON_TYPES) == "http://x/thumb"
    assert auto._first_url(kis, ["Nope"]) is None


def test_ranked_prefers_larger_area_and_video_covers():
    kis = [
        {"type": "OfferImageTall", "url": "http://x/small-400x530-a"},
        {"type": "OfferImageTall", "url": "http://x/big-1200x1600-b"},
        {"type": "heroCarouselVideo",
         "url": "com.epicgames.video://id?cover=https%3A%2F%2Fx%2Fcover-2560x1440-c.jpg"},
    ]
    ranked = auto._ranked_urls(kis, auto.EPIC_GRID_TYPES, 600 / 900)
    assert ranked[0] == "http://x/big-1200x1600-b"
    assert "https://x/cover-2560x1440-c.jpg" in ranked
    assert auto._video_cover("http://x/nocover") is None


def test_exact_match_case_insensitive():
    items = [{"title": "Cyberpunk SFX"}, {"title": "Fortnite"}]
    assert auto._exact("fortnite", items)["title"] == "Fortnite"
    assert auto._exact("Fortnite 2", items) is None


def test_cover_resize_aspect_and_size():
    out = auto._cover_resize(_img(1600, 900), (600, 900))
    assert out.size == (600, 900)
    out = auto._cover_resize(_img(400, 300), (1920, 620))
    assert out.size == (1920, 620)


def test_materialize_writes_all_four_types(tmp_path, monkeypatch):
    monkeypatch.setattr(auto, "_download", lambda url, s=None: _img())
    saved = auto._materialize(
        {"grids": "u1", "heroes": "u2", "logos": "u3"}, 12345, tmp_path)
    assert set(saved) == {"grids", "heroes", "logos", "icons"}
    assert (tmp_path / "12345p.jpg").is_file()
    assert (tmp_path / "12345_hero.jpg").is_file()
    assert (tmp_path / "12345_logo.png").is_file()
    assert (tmp_path / "12345.ico").is_file()
    grid = Image.open(tmp_path / "12345p.jpg")
    assert grid.size == (600, 900)
    hero = Image.open(tmp_path / "12345_hero.jpg")
    assert hero.size == (1920, 620)


def test_epic_urls_type_mapping():
    p = auto.EpicProvider()
    urls = p.urls({"keyImages": [
        {"type": "OfferImageTall", "url": "t"},
        {"type": "OfferImageWide", "url": "w"},
        {"type": "ProductLogo", "url": "l"},
        {"type": "Thumbnail", "url": "i"},
        {"type": "heroCarouselVideo", "url": "v"},
    ]})
    assert urls["grids"][0] == "t"
    assert urls["heroes"][0] == "w"
    assert urls["logos"] == ["l"]
    assert urls["icons"] == ["i"]

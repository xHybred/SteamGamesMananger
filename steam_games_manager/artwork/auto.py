"""Fully automatic (keyless) artwork providers with strict type mapping.

Priority per game: source-native provider first (Epic games → Epic catalog,
GOG games → GOG catalog), then the Steam store as a universal fallback.
SteamGridDB (keyed) remains the optional quality upgrade and is applied
first by the caller; this module fills whatever kinds are still missing.

Type mapping (icon→icon, banner→banner) is enforced per provider:
  grids  = vertical cover art   → <appid>p.jpg      (600x900 JPEG)
  heroes = wide banner art      → <appid>_hero.jpg   (1920x620 JPEG)
  logos  = transparent logos   → <appid>_logo.png   (PNG kept)
  icons  = small square art     → <appid>.ico        (256px ICO)

Only exact (case-insensitive) title matches are used automatically.
"""
from __future__ import annotations

import io
import logging
import os
import re
from pathlib import Path

import requests
from PIL import Image, ImageEnhance, ImageFilter

log = logging.getLogger(__name__)

UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
TIMEOUT = 25

GRID_SIZE = (600, 900)
HERO_SIZE = (1920, 620)
ICON_SIZE = (256, 256)

VIDEO_TYPES = {"herocarouselvideo", "videotrailer", "video"}

EPIC_GRID_TYPES = ["DieselGameBoxTall", "OfferImageTall", "DieselStoreFrontTall"]
EPIC_HERO_TYPES = ["DieselStoreFrontWide", "OfferImageWide", "DieselGameBoxWide"]
EPIC_LOGO_TYPES = ["DieselGameBoxLogo", "ProductLogo", "DieselStoreFrontLogo"]
EPIC_ICON_TYPES = ["Thumbnail"]
# Landscape art usable for either slot once aspect-checked:
EPIC_EXTRA_TYPES = ["featuredMedia"]


def _session() -> requests.Session:
    s = requests.Session()
    s.headers.update(UA)
    return s


def _download(url: str, session: requests.Session | None = None) -> Image.Image | None:
    """Load an image from a URL, or from a local file path (store caches)."""
    try:
        if os.path.isfile(url):
            img = Image.open(url)
            img.load()
            return img.convert("RGBA")
        s = session or _session()
        r = s.get(url, timeout=TIMEOUT)
        r.raise_for_status()
        img = Image.open(io.BytesIO(r.content))
        img.load()
        return img.convert("RGBA")
    except Exception as exc:
        log.debug("download failed %s: %s", url, exc)
        return None


def _cover_resize(img: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Center-crop to the target aspect ratio, then high-quality resize."""
    tw, th = size
    sw, sh = img.size
    scale = max(tw / sw, th / sh)
    img = img.resize((round(sw * scale), round(sh * scale)), Image.LANCZOS)
    x = (img.width - tw) // 2
    y = (img.height - th) // 2
    return img.crop((x, y, x + tw, y + th))


def _save_jpg(img: Image.Image, dest: Path) -> bool:
    try:
        dest.parent.mkdir(parents=True, exist_ok=True)
        img.convert("RGB").save(dest, "JPEG", quality=90)
        return True
    except Exception as exc:
        log.debug("save failed %s: %s", dest, exc)
        return False


def _save_png(img: Image.Image, dest: Path, max_width: int = 1024) -> bool:
    try:
        dest.parent.mkdir(parents=True, exist_ok=True)
        if img.width > max_width:
            img = img.resize((max_width, round(img.height * max_width / img.width)), Image.LANCZOS)
        img.save(dest, "PNG")
        return True
    except Exception as exc:
        log.debug("save failed %s: %s", dest, exc)
        return False


def _save_ico(img: Image.Image, dest: Path) -> bool:
    try:
        dest.parent.mkdir(parents=True, exist_ok=True)
        img.convert("RGB").resize(ICON_SIZE, Image.LANCZOS).save(dest, "ICO", sizes=[ICON_SIZE])
        return True
    except Exception as exc:
        log.debug("save failed %s: %s", dest, exc)
        return False


def _norm(title: str) -> str:
    """Compare-friendly title: lowercase, alphanumeric words only."""
    return " ".join(re.findall(r"[a-z0-9]+", title.strip().lower()))


def _exact(title: str, items: list[dict], key: str = "title") -> dict | None:
    want = _norm(title)
    if not want:
        return None
    for it in items:
        if _norm(str(it.get(key, ""))) == want:
            return it
    return None


def _first_url(key_images: list[dict], wanted: list[str]) -> str | None:
    ranked = _ranked_urls(key_images, wanted, GRID_SIZE[0] / GRID_SIZE[1])
    return ranked[0] if ranked else None


_DIM_RE = re.compile(r"(\d{3,5})\s*x\s*(\d{3,5})")


def _url_area(url: str) -> int:
    """Pixel area hint parsed from CDN URLs (e.g. '...-2560x1440-...')."""
    m = _DIM_RE.search(url)
    if not m:
        return 0
    return int(m.group(1)) * int(m.group(2))


def _video_cover(url: str) -> str | None:
    """Static `cover=` image carried by Epic video entries (URL-decoded)."""
    from urllib.parse import parse_qs, urlsplit, unquote
    try:
        qs = parse_qs(urlsplit(url).query)
        cover = qs.get("cover", [None])[0]
        if cover and cover.startswith("http"):
            return unquote(cover)
    except Exception:
        pass
    return None


def _ranked_urls(key_images: list[dict], wanted: list[str], target_aspect: float,
                 extras: bool = True) -> list[str]:
    """Candidate image URLs, best first.

    Order: exact `wanted` types first (bigger advertised area wins ties),
    then landscape `featuredMedia` shots and video-cover stills ranked by how
    close their advertised dimensions sit to the target aspect ratio.
    """
    primary: list[tuple[int, str]] = []
    spare: list[tuple[float, int, str]] = []
    for ki in key_images or []:
        t = str(ki.get("type", ""))
        url = str(ki.get("url", ""))
        if not url:
            continue
        if t.lower() in VIDEO_TYPES:
            cover = _video_cover(url)
            if cover and extras:
                spare.append((abs((16 / 9) / target_aspect - 1), -_url_area(cover), cover))
            continue
        if t in wanted:
            primary.append((-_url_area(url), url))
        elif extras and t in EPIC_EXTRA_TYPES:
            w_h = _DIM_RE.search(url)
            if w_h:
                aspect = int(w_h.group(1)) / int(w_h.group(2))
                spare.append((abs(aspect / target_aspect - 1),
                              -int(w_h.group(1)) * int(w_h.group(2)), url))
            else:
                spare.append((1.0, 0, url))
    primary.sort()
    spare.sort()
    out = [u for _, u in primary] + [u for _, _, u in spare]
    seen: set[str] = set()
    deduped = [u for u in out if not (u in seen or seen.add(u))]
    return deduped


class EpicProvider:
    """Epic Games Store catalog (public GraphQL, no key)."""

    name = "epic"

    def __init__(self) -> None:
        self._api = None

    def _client(self):
        if self._api is None:
            from epicstore_api import EpicGamesStoreAPI
            self._api = EpicGamesStoreAPI()
        return self._api

    def match(self, title: str) -> dict | None:
        try:
            d = self._client().fetch_store_games(keywords=title, count=10)
            els = d["data"]["Catalog"]["searchStore"]["elements"]
        except Exception as exc:
            log.debug("Epic search failed for %r: %s", title, exc)
            return None
        want = _norm(title)
        exact = [e for e in els if want and _norm(str(e.get("title", ""))) == want]
        if not exact:
            return None
        # Several entries can share a title (editions/DLC hubs): take the one
        # with the most complete, highest-resolution usable artwork.
        return max(exact, key=_epic_score)

    def urls(self, element: dict) -> dict[str, list[str]]:
        """Ranked image URL candidates per kind (best first)."""
        key_images = element.get("keyImages") or []
        out: dict[str, list[str]] = {}
        grid = _ranked_urls(key_images, EPIC_GRID_TYPES, GRID_SIZE[0] / GRID_SIZE[1])
        hero = _ranked_urls(key_images, EPIC_HERO_TYPES, HERO_SIZE[0] / HERO_SIZE[1])
        logo = _ranked_urls(key_images, EPIC_LOGO_TYPES, 0, extras=False)
        icon = _ranked_urls(key_images, EPIC_ICON_TYPES, 1, extras=False)
        if grid:
            out["grids"] = grid
        if hero:
            out["heroes"] = hero
        if logo:
            out["logos"] = logo
        if icon:
            out["icons"] = icon
        return out


def _epic_score(element: dict) -> tuple[int, int]:
    """Rank same-titled Epic entries: most complete artwork, then resolution."""
    key_images = element.get("keyImages") or []
    types = {str(k.get("type", "")) for k in key_images}
    kinds = 0
    for pool in (EPIC_GRID_TYPES, EPIC_HERO_TYPES, EPIC_LOGO_TYPES, EPIC_ICON_TYPES):
        if any(t in types for t in pool):
            kinds += 1
    if any(str(k.get("type", "")).lower() in VIDEO_TYPES or
           str(k.get("type", "")) in EPIC_EXTRA_TYPES for k in key_images):
        kinds += 1
    area = sum(_url_area(str(k.get("url", ""))) for k in key_images)
    return (kinds, area)


class SteamProvider:
    """Steam store search + CDN (public, no key)."""

    name = "steam"

    def match(self, title: str) -> dict | None:
        try:
            r = _session().get(
                "https://store.steampowered.com/api/storesearch/",
                params={"term": title, "l": "english", "cc": "US"}, timeout=TIMEOUT)
            r.raise_for_status()
            items = r.json().get("items", [])
        except Exception as exc:
            log.debug("Steam search failed for %r: %s", title, exc)
            return None
        hit = _exact(title, items, key="name")
        return {"appid": hit["id"], "name": hit["name"]} if hit else None

    def urls(self, match: dict) -> dict[str, list[str]]:
        appid = match["appid"]
        base = f"https://cdn.akamai.steamstatic.com/steam/apps/{appid}"
        return {"grids": [f"{base}/library_600x900.jpg"],
                "heroes": [f"{base}/library_hero.jpg"],
                "logos": [f"{base}/logo.png"]}


class GogProvider:
    """GOG catalog API (public, no key)."""

    name = "gog"

    def match(self, title: str) -> dict | None:
        try:
            r = _session().get(
                "https://catalog.gog.com/v1/catalog",
                params={"limit": 10, "query": title, "order": "desc:score"}, timeout=TIMEOUT)
            r.raise_for_status()
            prods = r.json().get("products", [])
        except Exception as exc:
            log.debug("GOG search failed for %r: %s", title, exc)
            return None
        return _exact(title, prods)

    def urls(self, product: dict) -> dict[str, list[str]]:
        out: dict[str, list[str]] = {}
        if product.get("coverVertical"):
            out["grids"] = [str(product["coverVertical"])]
        hero = product.get("galaxyBackgroundImage") or product.get("coverHorizontal")
        if hero:
            out["heroes"] = [str(hero)]
        if product.get("logo"):
            out["logos"] = [str(product["logo"])]
        return out


def _materialize(urls: dict[str, str | list[str]], appid: int, grid_dir: Path,
                 session: requests.Session | None = None) -> dict[str, Path]:
    """Download each kind from its first working candidate URL."""
    saved: dict[str, Path] = {}
    s = session or _session()

    def first_image(cands: str | list[str]) -> Image.Image | None:
        seq = [cands] if isinstance(cands, str) else list(cands)
        for url in seq:
            img = _download(url, s)
            if img is not None:
                return img
        return None

    if urls.get("grids"):
        img = first_image(urls["grids"])
        if img is not None:
            dest = grid_dir / f"{appid}p.jpg"
            if _save_jpg(_cover_resize(img, GRID_SIZE), dest):
                saved["grids"] = dest
    elif urls.get("squares"):
        cover = _square_cover_image(urls, first_image)
        if cover is not None:
            dest = grid_dir / f"{appid}p.jpg"
            if _save_jpg(cover, dest):
                saved["grids"] = dest
    if urls.get("heroes"):
        img = first_image(urls["heroes"])
        if img is not None:
            dest = grid_dir / f"{appid}_hero.jpg"
            if _save_jpg(_cover_resize(img, HERO_SIZE), dest):
                saved["heroes"] = dest
    if urls.get("logos"):
        img = first_image(urls["logos"])
        if img is not None:
            img = _trim_transparent(img)
            dest = grid_dir / f"{appid}_logo.png"
            if _save_png(img, dest):
                saved["logos"] = dest
    icon_cands = urls.get("icons") or urls.get("grids") or urls.get("squares") or urls.get("logos")
    if icon_cands:
        img = first_image(icon_cands)
        if img is not None:
            dest = grid_dir / f"{appid}.ico"
            if _save_ico(_square_crop(img), dest):
                saved["icons"] = dest
    return saved


def _compose_square_cover(square: Image.Image, backdrop: Image.Image | None,
                          size: tuple[int, int]) -> Image.Image:
    """Square logo centered over a dimmed, blurred splash (or a flat dark field)."""
    tw, th = size
    if backdrop is not None:
        base = _cover_resize(backdrop.convert("RGB"), size).filter(ImageFilter.GaussianBlur(14))
        base = ImageEnhance.Brightness(base).enhance(0.5).convert("RGBA")
    else:
        base = Image.new("RGBA", size, (27, 40, 56, 255))
    side = round(tw * 0.8)
    mark = _square_crop(square.convert("RGBA")).resize((side, side), Image.LANCZOS)
    base.alpha_composite(mark, ((tw - side) // 2, (th - side) // 2))
    return base.convert("RGB")


def _square_cover_image(urls: dict, fetch) -> Image.Image | None:
    """Cover built from square art (`squares`) plus the splash (`heroes`) as backdrop."""
    square = fetch(urls["squares"])
    if square is None:
        return None
    backdrop = fetch(urls["heroes"]) if urls.get("heroes") else None
    return _compose_square_cover(square, backdrop, GRID_SIZE)


def local_thumb_image(art_urls: dict[str, str]) -> Image.Image | None:
    """Cover image from a game's own store art, for the library row thumbnail."""
    if art_urls.get("grids"):
        return _download(art_urls["grids"])
    if art_urls.get("squares"):
        return _square_cover_image(
            {k: v for k, v in art_urls.items() if k in ("squares", "heroes")}, _download)
    return None


def _trim_transparent(img: Image.Image) -> Image.Image:
    """Crop a logo to its visible pixels so padding never pushes the mark off-center."""
    rgba = img.convert("RGBA")
    box = rgba.getchannel("A").getbbox()
    return rgba.crop(box) if box else rgba


def _square_crop(img: Image.Image) -> Image.Image:
    """Center-crop to a square so icons never come out stretched."""
    side = min(img.size)
    x = (img.width - side) // 2
    y = (img.height - side) // 2
    return img.crop((x, y, x + side, y + side))


def fetch_auto_artwork(game_name: str, source: str, appid: int, grid_dir: Path,
                       missing: set[str] | None = None,
                       local_urls: dict[str, str] | None = None) -> tuple[dict[str, Path], str | None]:
    """Best-effort keyless artwork. Returns (saved, provider_name|None).

    Only fills kinds in `missing` (None = all four). Source-native provider
    first, Steam store fallback second.
    """
    want = missing if missing is not None else {"grids", "heroes", "logos", "icons"}
    if not want:
        return {}, None
    session = _session()
    if local_urls:
        local = {k: [u] for k, u in local_urls.items()
                 if u and (k in want or (k == "squares" and "grids" in want))}
        if local:
            saved = _materialize(local, appid, grid_dir, session)
            if saved:
                log.info("Auto-art: %r <- local store cache (%s)", game_name,
                         ", ".join(sorted(saved)))
                remaining = want - set(saved)
                if not remaining:
                    return saved, "local"
                more, provider = fetch_auto_artwork(game_name, source, appid, grid_dir,
                                                    remaining, None)
                saved.update(more)
                return saved, provider or "local"
    providers: list = []
    if source == "epic":
        providers.append(EpicProvider())
    elif source == "gog":
        providers.append(GogProvider())
    providers.append(SteamProvider())
    for p in providers:
        try:
            match = p.match(game_name)
        except Exception as exc:
            log.debug("%s provider error for %r: %s", p.name, game_name, exc)
            continue
        if match is None:
            continue
        urls = {k: u for k, u in p.urls(match).items() if k in want}
        if not urls:
            continue
        saved = _materialize(urls, appid, grid_dir, session)
        if saved:
            log.info("Auto-art: %r <- %s (%s)", game_name, p.name, ", ".join(sorted(saved)))
            return saved, p.name
        log.info("Auto-art: %r matched on %s but downloads failed", game_name, p.name)
    log.info("Auto-art: no exact match for %r", game_name)
    return {}, None


def auto_match_label(game_name: str, source: str) -> str:
    """One-line description of the automatic match (for the GUI dialog)."""
    providers: list = []
    if source == "epic":
        providers.append(EpicProvider())
    elif source == "gog":
        providers.append(GogProvider())
    providers.append(SteamProvider())
    for p in providers:
        try:
            m = p.match(game_name)
        except Exception:
            continue
        if m is not None:
            title = m.get("title") or m.get("name") or "?"
            return f"Auto ({p.name}): {title}"
    return "Auto: no exact match found"


def thumbnail_url(game_name: str, source: str) -> str | None:
    """Best small portrait-ish image URL for Library card thumbnails."""
    providers: list = []
    if source == "epic":
        providers.append(EpicProvider())
    elif source == "gog":
        providers.append(GogProvider())
    providers.append(SteamProvider())
    for p in providers:
        try:
            m = p.match(game_name)
        except Exception:
            continue
        if m is None:
            continue
        urls = p.urls(m)
        for kind in ("icons", "grids", "heroes", "logos"):
            if urls.get(kind):
                return urls[kind][0]
    return None

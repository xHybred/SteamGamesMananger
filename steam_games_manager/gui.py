"""Main window: a Steam-styled library of detected games with cover art."""
from __future__ import annotations

import logging
import math
import os
import queue
import sys
import threading
import tkinter as tk
from pathlib import Path

import customtkinter as ctk
from PIL import Image, ImageDraw, ImageFont

from . import settings

log = logging.getLogger("sgm.gui")

ctk.set_appearance_mode("dark")

BG = "#1b2838"
CHROME = "#171a21"
SIDEBAR = "#171d25"
CARD = "#1e2b3c"
CARD_EDGE = "#2a475e"
WELL = "#131b26"
PANEL = "#2a475e"
PANEL_HOVER = "#3d6586"
ROW_HOVER = "#2a3f5a"
ACCENT = "#1a9fff"
ACCENT_HOVER = "#47bfff"
ACCENT_INK = "#0b1620"
LINK = "#66c0f4"
TEXT = "#c6d4df"
TEXT_STRONG = "#f5f9fc"
MUTED = "#8f98a0"
DIM = "#556772"
SCROLL_THUMB = "#3d5870"
SCROLL_THUMB_HOVER = "#5b7a95"
GOOD = "#7fc97f"
BAD = "#e06c5b"

FONT = "Segoe UI"
RADIUS = 4
TILE_RADIUS = 6

SOURCE_LABELS = {"epic": "Epic", "gog": "GOG", "xbox": "Xbox", "ubisoft": "Ubisoft",
                 "ea": "EA", "battlenet": "Battle.net", "desktop": "Desktop"}
SOURCE_ORDER = ("epic", "gog", "xbox", "ubisoft", "ea", "battlenet", "desktop")
SOURCE_COL_W = 72
STATUS_COL_W = 250

_ICON_CACHE: dict = {}


def icon(name: str, color: str = "#c7d5e0", size: int = 16):
    """Pillow-drawn UI glyph, cached per name, color and size."""
    key = (name, color, size)
    if key in _ICON_CACHE:
        return _ICON_CACHE[key]
    s, sup = size, 4
    img = Image.new("RGBA", (s * sup, s * sup), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = s * sup / 16  # 16-unit grid

    def bar(x0, y0, x1, y1):
        d.rounded_rectangle([x0 * u, y0 * u, x1 * u, y1 * u], radius=1.2 * u, fill=color)

    if name == "plus":
        bar(7, 2.5, 9, 13.5)
        bar(2.5, 7, 13.5, 9)
    elif name == "dot":
        d.ellipse([3 * u, 3 * u, 13 * u, 13 * u], fill=color)
    elif name == "hide":
        d.line([4 * u, 4 * u, 12 * u, 12 * u], fill=color, width=round(2 * u))
        d.line([12 * u, 4 * u, 4 * u, 12 * u], fill=color, width=round(2 * u))
    elif name == "picture":
        d.rounded_rectangle([2 * u, 3 * u, 14 * u, 13 * u], radius=2 * u, outline=color,
                            width=round(1.6 * u))
        d.ellipse([4.4 * u, 5 * u, 6.4 * u, 7 * u], fill=color)
        d.polygon([(2.5 * u, 12.5 * u), (7 * u, 7.5 * u), (10 * u, 10.5 * u),
                   (12 * u, 8.5 * u), (13.5 * u, 10 * u), (13.5 * u, 12.5 * u)], fill=color)
    elif name == "grid":
        for cx, cy in ((4.6, 4.6), (11.4, 4.6), (4.6, 11.4), (11.4, 11.4)):
            d.rounded_rectangle([(cx - 2.4) * u, (cy - 2.4) * u,
                                (cx + 2.4) * u, (cy + 2.4) * u],
                               radius=1.2 * u, fill=color)
    elif name == "list":
        for cy in (4, 8, 12):
            d.ellipse([2.2 * u, (cy - 1.1) * u, 4.4 * u, (cy + 1.1) * u], fill=color)
            bar(6, cy - 1.1, 14, cy + 1.1)
    elif name == "user":
        d.ellipse([5.5 * u, 2.5 * u, 10.5 * u, 7.5 * u], fill=color)
        d.chord([3 * u, 8 * u, 13 * u, 16 * u], start=180, end=360, fill=color)
    elif name == "gear":
        for a in range(8):
            r = math.radians(a * 45)
            d.line([8 * u + 3.4 * u * math.cos(r), 8 * u + 3.4 * u * math.sin(r),
                    8 * u + 6 * u * math.cos(r), 8 * u + 6 * u * math.sin(r)],
                   fill=color, width=round(1.8 * u))
        d.ellipse([5 * u, 5 * u, 11 * u, 11 * u], fill=color)
        d.ellipse([6.8 * u, 6.8 * u, 9.2 * u, 9.2 * u], fill=(0, 0, 0, 0))
    elif name == "help":
        try:
            font = ImageFont.truetype("segoeui.ttf", round(11 * u))
            d.text((8 * u, 8 * u), "?", fill=color, font=font, anchor="mm")
        except Exception:
            d.ellipse([11 * u, 11.5 * u, 13 * u, 13.5 * u], fill=color)
            bar(7, 3, 9, 9)
    else:
        bar(3, 7, 13, 9)
    img = img.resize((size, size), Image.LANCZOS)
    photo = ctk.CTkImage(light_image=img, dark_image=img, size=(size, size))
    _ICON_CACHE[key] = photo
    return photo


def _mix_hex(a: str, b: str, t: float) -> str:
    """Linear blend of two #rrggbb colors, t in [0, 1]."""
    ca = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    cb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(x + (y - x) * t):02x}" for x, y in zip(ca, cb))


def ease_fg(widget, target: str, steps: int = 5) -> None:
    """Blend a button's fill to `target` over a few frames; snaps when either side is not a hex color."""
    prev = getattr(widget, "_ease_job", None)
    if prev is not None:
        try:
            widget.after_cancel(prev)
        except Exception:
            pass
        widget._ease_job = None
    start = widget.cget("fg_color")
    if not (isinstance(start, str) and start.startswith("#") and target.startswith("#")):
        widget.configure(fg_color=target)
        return

    def step(i: int) -> None:
        widget._ease_job = None
        widget.configure(fg_color=_mix_hex(start, target, i / steps))
        if i < steps:
            try:
                widget._ease_job = widget.after(16, step, i + 1)
            except Exception:
                widget.configure(fg_color=target)

    step(1)


def cover_fade(img: Image.Image) -> Image.Image:
    """Dark gradient over the lower part of cover art, as in Steam's library tiles."""
    img = img.convert("RGBA")
    w, h = img.size
    start = int(h * 0.55)
    ramp = Image.new("L", (1, h), 0)
    ramp.putdata([0 if y < start else int(200 * (y - start) / max(1, h - start))
                  for y in range(h)])
    shade = Image.new("RGBA", (w, h), (23, 29, 37, 255))
    shade.putalpha(ramp.resize((w, h)))
    return Image.alpha_composite(img, shade)


def app_base_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path.cwd()


def cache_dir() -> Path:
    d = app_base_dir() / ".sgm_cache"
    d.mkdir(exist_ok=True)
    return d


def asset_path(name: str) -> Path | None:
    """Locate a bundled asset in dev and in a PyInstaller onefile build."""
    roots = []
    if getattr(sys, "frozen", False):
        roots.append(Path(getattr(sys, "_MEIPASS", app_base_dir())))
        roots.append(app_base_dir())
    roots.append(Path(__file__).resolve().parent.parent)
    for root in roots:
        cand = root / name
        if cand.is_file():
            return cand
    return None


def setup_file_logging() -> Path:
    path = app_base_dir() / "SteamGamesManager.log"
    handler = logging.FileHandler(path, encoding="utf-8")
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s %(name)s: %(message)s"))
    logging.getLogger("sgm").addHandler(handler)
    return path


class QueueHandler(logging.Handler):
    def __init__(self, q: queue.Queue[str]) -> None:
        super().__init__()
        self.q = q

    def emit(self, record: logging.LogRecord) -> None:
        try:
            self.q.put(self.format(record))
        except Exception:
            pass


def gkey(g) -> tuple[str, str, str]:
    return (g.source, g.name, g.exe)


def visible_games(games: list, sources: dict[str, bool], needle: str) -> list:
    """Filter by launcher chips + text. Pure; powers cards and compact rows."""
    needle = needle.lower()
    return [g for g in games
            if sources.get(g.source, False) and (not needle or needle in g.name.lower())]


def source_counts(games: list) -> dict[str, int]:
    counts: dict[str, int] = {}
    for g in games:
        counts[g.source] = counts.get(g.source, 0) + 1
    return counts


def in_steam_set(data: dict) -> set[tuple[str, str]]:
    """(AppName, Exe) pairs present in one shortcuts.vdf dict."""
    out: set[tuple[str, str]] = set()
    for v in data.get("shortcuts", {}).values():
        if isinstance(v, dict):
            out.add((str(v.get("AppName", "")), str(v.get("Exe", ""))))
    return out


def is_in_steam(game, states: list[set[tuple[str, str]]]) -> bool:
    from .vdf_handler.shortcuts import to_vdf_entry
    entry = to_vdf_entry(game)
    pair = (entry["AppName"], entry["Exe"])
    return any(pair in s for s in states)


def summary_counts(games: list, in_steam: set) -> tuple[int, int]:
    new = sum(1 for g in games if gkey(g) not in in_steam)
    return len(games), new


class MatchDialog(ctk.CTkToplevel):
    def __init__(self, parent, game_name: str, candidates: list[dict],
                 auto_info: str = "") -> None:
        super().__init__(parent)
        self.title(f"Match Artwork — {game_name}")
        self.geometry("500x460")
        self.configure(fg_color=BG)
        self.transient(parent)
        self.grab_set()
        self.result: dict | None = None

        ctk.CTkLabel(self, text=f"Match Artwork — {game_name}",
                     font=(FONT, 14, "bold"), text_color=TEXT).pack(padx=16, pady=(14, 4))
        if auto_info:
            ctk.CTkLabel(self, text=auto_info, font=(FONT, 11),
                         text_color=MUTED).pack(padx=16, pady=(0, 6))
        body = ctk.CTkScrollableFrame(self, fg_color=CARD)
        body.pack(fill="both", expand=True, padx=16, pady=4)
        self.choice = tk.IntVar(value=-1)
        if not candidates:
            ctk.CTkLabel(body, text="No SteamGridDB Results (key missing or no match).",
                         font=(FONT, 11), text_color=MUTED).pack(padx=8, pady=8)
        for c in candidates[:15]:
            name = str(c.get("name", "?"))
            rel = str(c.get("release_date", "") or "")
            label = f"{name}" + (f"  ·  {rel}" if rel else "")
            ctk.CTkRadioButton(body, text=label, variable=self.choice, value=int(c["id"]),
                               font=(FONT, 11)).pack(anchor="w", padx=8, pady=3)
        row = ctk.CTkFrame(self, fg_color="transparent")
        row.pack(fill="x", padx=16, pady=12)
        ctk.CTkButton(row, text="Use Selected", fg_color=ACCENT, hover_color=ACCENT_HOVER,
                      text_color=ACCENT_INK, font=(FONT, 12, "bold"),
                      command=self._ok).pack(side="left", expand=True, padx=(0, 6))
        ctk.CTkButton(row, text="Cancel", fg_color=PANEL, hover_color=PANEL_HOVER,
                      font=(FONT, 12), command=self.destroy).pack(side="left", expand=True, padx=(6, 0))

    def _ok(self) -> None:
        if self.choice.get() == -1:
            return
        self.result = {"id": self.choice.get()}
        self.destroy()


class App(ctk.CTk):
    def __init__(self, msg_q: queue.Queue[str]) -> None:
        super().__init__()
        _ICON_CACHE.clear()
        self.title("Steam Games Manager")
        self.geometry("1080x740")
        self.minsize(900, 620)
        self.configure(fg_color=BG)

        self.msg_q = msg_q
        self.working = False
        self.steam_dir: Path | None = None
        self.steam_running = False
        self.accounts: list[tuple[Path, str, bool]] = []
        self.account_vars: list[tuple[Path, tk.BooleanVar]] = []
        self.games: list = []
        self.overrides: dict[tuple[str, str, str], tuple[int, str]] = {}
        self.art_status: dict[tuple[str, str, str], str] = {}
        self.steam_state: set[tuple[str, str, str]] = set()
        self.steam_titles: set[str] = set()
        self.thumbs: dict = {}
        self.thumb_paths: dict[tuple[str, str, str], str] = {}
        self.row_widgets: dict[tuple[str, str, str], dict] = {}
        self.search_var = tk.StringVar(value="")
        self._settings = settings.load()
        self.sources = dict(self._settings["sources"])
        self.view_mode = tk.StringVar(value=self._settings.get("view", "list"))
        self.density = tk.StringVar(value=self._settings.get("density", "compact"))
        self.chips_var = tk.StringVar(value="")
        self.hidden: set[tuple[str, str, str]] = {
            tuple(h) for h in self._settings.get("hidden", [])}
        self.artwork_var = tk.BooleanVar(value=bool(self._settings.get("artwork", True)))
        self.overwrite_var = tk.BooleanVar(value=bool(self._settings.get("overwrite", True)))
        self.force_var = tk.BooleanVar(value=bool(self._settings.get("force", False)))
        self._key_var = tk.StringVar(
            value=str(self._settings.get("api_key", "") or os.environ.get("STEAMGRIDDB_API_KEY", "")))
        for var in (self.artwork_var, self.overwrite_var, self.force_var):
            var.trace_add("write", lambda *_a: self._save_settings())
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.key_state = tk.StringVar(value="")
        self.view = "library"

        self._build()
        self._poll()
        self._run_thread("Detecting Steam…", self._detect_env)

    def _build(self) -> None:
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        side = ctk.CTkFrame(self, fg_color=SIDEBAR, width=104, corner_radius=0)
        side.grid(row=0, column=0, sticky="nsew")
        side.grid_propagate(False)
        brand = ctk.CTkFrame(side, fg_color="transparent")
        brand.pack(fill="x", padx=(20, 10), pady=(20, 18))
        ctk.CTkLabel(brand, text="SGM", font=("", 18, "bold"),
                     text_color=TEXT_STRONG, anchor="w").pack(side="left")
        self.nav_btns: dict[str, dict] = {}
        for name, glyph in (("Library", "grid"), ("Accounts", "user"),
                              ("Settings", "gear"), ("Help", "help")):
            key = name.lower()
            item = ctk.CTkFrame(side, fg_color="transparent", corner_radius=8, height=52)
            item.pack(fill="x", padx=(10, 10), pady=4)
            item.pack_propagate(False)
            edge = ctk.CTkFrame(item, fg_color="transparent", width=3, corner_radius=0)
            edge.pack(side="left", fill="y")
            btn = ctk.CTkButton(item, text=name, image=icon(glyph, MUTED, 16), compound="left",
                                anchor="w", font=(FONT, 12), height=52,
                                fg_color="transparent", hover_color=CARD, text_color=MUTED,
                                command=lambda n=key: self.show_view(n))
            btn.pack(side="left", fill="both", expand=True, padx=(6, 0))
            self.nav_btns[key] = {"item": item, "edge": edge, "btn": btn,
                                  "glyph": glyph}

        self.views: dict[str, ctk.CTkFrame] = {}
        for name in ("library", "accounts", "settings", "help"):
            f = ctk.CTkFrame(self, fg_color="transparent")
            f.grid(row=0, column=1, sticky="nsew", padx=20, pady=14)
            f.grid_remove()
            self.views[name] = f
        self._build_library(self.views["library"])
        self._build_accounts(self.views["accounts"])
        self._build_settings(self.views["settings"])
        self._build_help(self.views["help"])
        self.show_view("library")

    def show_view(self, name: str) -> None:
        self.view = name
        for n, f in self.views.items():
            f.grid_remove() if n != name else f.grid()
        for n, parts in self.nav_btns.items():
            active = n == name
            parts["item"].configure(fg_color=CARD if active else "transparent")
            parts["edge"].configure(fg_color=ACCENT if active else "transparent")
            parts["btn"].configure(
                text_color=TEXT_STRONG if active else MUTED,
                image=icon(parts["glyph"], ACCENT if active else MUTED, 16))

    def _build_library(self, root: ctk.CTkFrame) -> None:
        root.grid_columnconfigure(0, weight=1)
        root.grid_rowconfigure(2, weight=1)
        head = ctk.CTkFrame(root, fg_color="transparent")
        head.grid(row=0, column=0, sticky="ew")
        head.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(head, text="Steam Games", font=("", 20, "bold"),
                     text_color=TEXT_STRONG).grid(row=0, column=0, sticky="w", padx=(4, 0), pady=4)
        search_wrap = ctk.CTkFrame(head, fg_color="transparent")
        search_wrap.grid(row=0, column=1, sticky="ew")
        search_wrap.grid_columnconfigure(0, weight=1)
        search = ctk.CTkEntry(search_wrap, textvariable=self.search_var, width=220, height=32,
                              placeholder_text="Search", font=(FONT, 12), justify="center",
                              corner_radius=RADIUS, fg_color=CARD, border_color=CARD_EDGE)
        search.grid(row=0, column=0, pady=4)
        search.bind("<KeyRelease>", lambda _e: self._render_games())
        actions = ctk.CTkFrame(head, fg_color="transparent")
        actions.grid(row=0, column=2, sticky="e")
        self.add_all_btn = ctk.CTkButton(actions, text="Add All New", width=130, height=32,
                                        corner_radius=RADIUS,
                                        fg_color=ACCENT, hover_color=ACCENT_HOVER,
                                        text_color=ACCENT_INK, text_color_disabled=ACCENT_INK,
                                        font=(FONT, 12, "bold"), command=self.add_all_new)
        self.add_all_btn.pack(side="right")
        ctk.CTkButton(actions, text="↻ Check Again", width=130, height=32, corner_radius=RADIUS,
                      fg_color=PANEL, hover_color=PANEL_HOVER, font=(FONT, 12),
                      command=self.rescan_games).pack(side="right", padx=(0, 8))
        self.summary_var = tk.StringVar(value="Scanning…")
        summary_row = ctk.CTkFrame(root, fg_color="transparent")
        summary_row.grid(row=1, column=0, sticky="ew", pady=(2, 8))
        ctk.CTkLabel(summary_row, textvariable=self.summary_var, font=(FONT, 11),
                     text_color=MUTED, anchor="w").pack(side="left", padx=(4, 0))
        self.steam_lbl = ctk.CTkLabel(summary_row, text="Steam: —", font=(FONT, 11),
                                      text_color=MUTED)
        self.steam_lbl.pack(side="right")
        self.flash_lbl = ctk.CTkLabel(summary_row, text="", font=(FONT, 11), text_color=GOOD)
        self._flash_job = None
        self.steam_dot = ctk.CTkLabel(summary_row, text="", image=icon("dot", MUTED, 10),
                                      width=14)
        self.steam_dot.pack(side="right", padx=(0, 6))
        toggles = ctk.CTkFrame(summary_row, fg_color="transparent")
        toggles.pack(side="right", padx=(0, 18))
        self.view_group: dict[str, ctk.CTkButton] = {}
        self._seg_group(toggles, 0, (("list", None, "list"), ("grid", None, "grid")),
                        self.view_mode.get(), self.view_group, self._pick_view)
        self.density_group: dict[str, ctk.CTkButton] = {}
        self._seg_group(toggles, 1, (("compact", "Compact", None), ("normal", "Normal", None)),
                        self.density.get(), self.density_group, self._pick_density)

        self.list_wrap = ctk.CTkFrame(root, fg_color="transparent")
        self.list_wrap.grid(row=2, column=0, sticky="nsew")
        self.list_wrap.grid_columnconfigure(0, weight=1)
        self.list_wrap.grid_rowconfigure(0, weight=1)
        self.list_canvas = tk.Canvas(self.list_wrap, bg=BG, highlightthickness=0,
                                     borderwidth=0)
        self.list_scroll = ctk.CTkScrollbar(self.list_wrap, orientation="vertical",
                                            width=8, corner_radius=4, fg_color=BG,
                                            button_color=SCROLL_THUMB,
                                            button_hover_color=SCROLL_THUMB_HOVER,
                                            command=self.list_canvas.yview)
        self.list_canvas.configure(yscrollcommand=self.list_scroll.set)
        self.list_canvas.grid(row=0, column=0, sticky="nsew")
        self._sb_shown = False
        self.game_box = ctk.CTkFrame(self.list_canvas, fg_color="transparent")
        self.game_box.grid_columnconfigure(0, weight=1)
        self._canvas_win = self.list_canvas.create_window((0, 0), window=self.game_box,
                                                           anchor="nw")
        self.game_box.bind("<Configure>", lambda _e: self._sync_scroll())
        self.list_canvas.bind("<Configure>",
                              lambda e: (self.list_canvas.itemconfig(
                                  self._canvas_win, width=e.width),
                                  self._sync_scroll()))
        self.list_canvas.configure(yscrollincrement=12)
        self.list_canvas.bind_all("<MouseWheel>", self._on_wheel)

        filt = ctk.CTkFrame(root, fg_color="transparent")
        filt.grid(row=3, column=0, sticky="ew", pady=(8, 0))
        filt.grid_columnconfigure(len(SOURCE_ORDER), weight=1)
        self.chip_btns: dict[str, ctk.CTkButton] = {}
        for i, src in enumerate(SOURCE_ORDER):
            b = ctk.CTkButton(filt, text=SOURCE_LABELS[src], width=88, height=28, font=(FONT, 10),
                              corner_radius=RADIUS,
                              command=lambda s=src: self._toggle_source(s))
            b.grid(row=0, column=i, padx=(0 if i == 0 else 4, 0), sticky="w")
            self.chip_btns[src] = b
        self.status_var = tk.StringVar(value="Ready.")
        self.status_lbl = ctk.CTkLabel(filt, textvariable=self.status_var, font=(FONT, 10),
                                       text_color=MUTED)
        self.status_lbl.grid(row=0, column=len(SOURCE_ORDER) + 1, padx=(12, 0), sticky="e")

    def _seg_group(self, parent, col, options: tuple, current: str,
                   store: dict, pick) -> None:
        """Toggle group: selected = blue bg + dark ink, else ghost.

        Each option is (key, text, glyph). An option with no text is icon-only.
        """
        widths = [34 if text is None else 66 for _key, text, _glyph in options]
        frame = ctk.CTkFrame(parent, fg_color=CARD, corner_radius=RADIUS + 1, height=28,
                             width=sum(widths) + 2 * len(options) + 2)
        frame.grid(row=0, column=col, padx=(6, 0), sticky="e")
        frame.pack_propagate(False)
        for (key, text, glyph), w in zip(options, widths):
            b = ctk.CTkButton(frame, text=text or "", width=w, height=24, font=(FONT, 10),
                              corner_radius=RADIUS, command=lambda k=key: pick(k))
            b.pack(side="left", padx=1, pady=2)
            b._glyph = glyph
            store[key] = b
        self._paint_seg(store, current)

    @staticmethod
    def _paint_seg(store: dict, current: str) -> None:
        for key, b in store.items():
            on = key == current
            ink = ACCENT_INK if on else MUTED
            glyph = getattr(b, "_glyph", None)
            if glyph:
                b.configure(image=icon(glyph, ink, 14))
            ease_fg(b, ACCENT if on else CARD)
            b.configure(hover_color=ACCENT_HOVER if on else PANEL,
                        text_color=ink)

    def _content_height(self) -> int:
        try:
            box = self.list_canvas.bbox("all")
            return box[3] - box[1] if box else 0
        except Exception:
            return 0

    def _sync_scroll(self) -> None:
        """Clamp the scroll region to the content; lock when all fits."""
        try:
            box = self.list_canvas.bbox("all")
            if not box:
                return
            self.list_canvas.configure(scrollregion=(0, 0, box[2], box[3]))
            top, bottom = self.list_canvas.yview()
            if box[3] - box[1] <= self.list_canvas.winfo_height():
                self.list_canvas.yview_moveto(0)
        except Exception:
            pass

    def _on_wheel(self, event) -> None:
        if self.view != "library":
            return
        try:
            widget = self.winfo_containing(event.x_root, event.y_root)
            node: object = widget
            inside = False
            while node is not None and node != self:
                if node is self.list_canvas or node is self.game_box:
                    inside = True
                    break
                node = getattr(node, "master", None)
            if not inside:
                return
            steps = -1 * (event.delta // 120) if abs(event.delta) >= 120 else 0
            if steps == 0:
                return
            top, bottom = self.list_canvas.yview()
            if (steps < 0 and top <= 0.0) or (steps > 0 and bottom >= 1.0):
                return
            self.list_canvas.yview_scroll(steps, "units")
        except Exception:
            pass

    def _paint_chips(self) -> None:
        counts = getattr(self, "_counts", {})
        for src, btn in self.chip_btns.items():
            on = self.sources.get(src, False)
            ease_fg(btn, ACCENT if on else BG)
            btn.configure(text=f"{SOURCE_LABELS[src]} [{counts.get(src, 0)}]",
                          hover_color=ACCENT_HOVER if on else PANEL,
                          text_color=ACCENT_INK if on else MUTED,
                          border_width=0 if on else 1, border_color=CARD_EDGE)

    def _toggle_source(self, src: str) -> None:
        self.sources[src] = not self.sources.get(src, False)
        self._paint_chips()
        self._save_settings()
        self._render_games()

    def _pick_view(self, value: str) -> None:
        self.view_mode.set(value)
        self._paint_seg(self.view_group, value)
        self._save_settings()
        self._render_games()

    def _pick_density(self, value: str) -> None:
        self.density.set(value)
        self._paint_seg(self.density_group, value)
        self._save_settings()
        self._render_games()

    def _save_settings(self) -> None:
        try:
            settings.save({
                "sources": dict(self.sources),
                "view": self.view_mode.get(),
                "density": self.density.get(),
                "artwork": bool(self.artwork_var.get()),
                "overwrite": bool(self.overwrite_var.get()),
                "force": bool(self.force_var.get()),
                "api_key": self._key_var.get().strip(),
                "hidden": sorted([list(h) for h in self.hidden]),
            })
        except Exception as exc:
            log.debug("settings save failed: %s", exc)

    def _on_close(self) -> None:
        self._save_settings()
        self.destroy()

    def hide_game(self, game) -> None:
        self.hidden.add(gkey(game))
        self._save_settings()
        self._refresh_hidden_label()
        self.say(f"Hidden: {game.name} (Settings → Unhide All to restore)")
        self._render_games()

    def unhide_all(self) -> None:
        self.hidden.clear()
        self._save_settings()
        self._refresh_hidden_label()
        self._render_games()

    def _refresh_hidden_label(self) -> None:
        if hasattr(self, "hidden_var"):
            self.after(0, lambda: self.hidden_var.set(f"{len(self.hidden)} Hidden Game(s)"))

    def _build_accounts(self, root: ctk.CTkFrame) -> None:
        ctk.CTkLabel(root, text="Accounts", font=("", 22, "bold"),
                     text_color=TEXT_STRONG).pack(anchor="w", padx=4, pady=(4, 0))
        ctk.CTkLabel(root, text="Shortcuts are written to checked accounts. ★ = most recent login.",
                     font=(FONT, 11), text_color=MUTED).pack(anchor="w", pady=(0, 10))
        self.acct_box = ctk.CTkFrame(root, fg_color=CARD, corner_radius=10)
        self.acct_box.pack(fill="x")
        ctk.CTkButton(root, text="Remove System Junk From Checked", width=280, height=34,
                      fg_color=PANEL, hover_color=PANEL_HOVER, font=(FONT, 12),
                      command=self.prune_accounts).pack(anchor="w", pady=12)

    def _build_settings(self, root: ctk.CTkFrame) -> None:
        ctk.CTkLabel(root, text="Settings", font=("", 22, "bold"),
                     text_color=TEXT_STRONG).pack(anchor="w", padx=4, pady=(4, 10))
        card = ctk.CTkFrame(root, fg_color=CARD, corner_radius=10)
        card.pack(fill="x", padx=2, pady=4)
        ctk.CTkLabel(card, text="SteamGridDB Key (optional — better custom art, auto art needs no key)",
                     font=(FONT, 11), text_color=MUTED).pack(anchor="w", padx=14, pady=(12, 4))
        row = ctk.CTkFrame(card, fg_color="transparent")
        row.pack(fill="x", padx=14, pady=(0, 6))
        if not hasattr(self, "_key_var"):
            self._key_var = tk.StringVar(value=os.environ.get("STEAMGRIDDB_API_KEY", ""))
        ctk.CTkEntry(row, textvariable=self._key_var, show="•", width=320, font=(FONT, 11),
                     fg_color=BG, border_color=CARD_EDGE).pack(side="left")
        ctk.CTkButton(row, text="Validate", width=90, fg_color=ACCENT, hover_color=ACCENT_HOVER,
                      text_color=ACCENT_INK, font=(FONT, 12, "bold"),
                      command=self.validate_key).pack(side="left", padx=8)
        ctk.CTkLabel(row, textvariable=self.key_state, font=(FONT, 11)).pack(side="left")
        opts = ctk.CTkFrame(root, fg_color=CARD, corner_radius=10)
        opts.pack(fill="x", padx=2, pady=8)
        for text, var in (("Download Artwork", self.artwork_var),
                          ("Overwrite Existing Shortcuts", self.overwrite_var),
                          ("Write Even If Steam Is Running", self.force_var)):
            ctk.CTkCheckBox(opts, text=text, variable=var, font=(FONT, 12),
                            fg_color=ACCENT, hover_color=ACCENT_HOVER,
                            text_color=TEXT).pack(anchor="w", padx=14, pady=6)
        hide_card = ctk.CTkFrame(root, fg_color=CARD, corner_radius=10)
        hide_card.pack(fill="x", padx=2, pady=4)
        self.hidden_var = tk.StringVar(value="")
        ctk.CTkLabel(hide_card, textvariable=self.hidden_var, font=(FONT, 11),
                     text_color=MUTED).pack(side="left", padx=14, pady=10)
        ctk.CTkButton(hide_card, text="Unhide All", width=100, fg_color=PANEL,
                      hover_color=PANEL_HOVER, font=(FONT, 11),
                      command=self.unhide_all).pack(side="right", padx=14, pady=6)
        self._refresh_hidden_label()

    def _build_help(self, root: ctk.CTkFrame) -> None:
        ctk.CTkLabel(root, text="Help", font=("", 22, "bold"),
                     text_color=TEXT_STRONG).pack(anchor="w", padx=4, pady=(4, 10))
        ctk.CTkLabel(root, font=(FONT, 11), text_color=MUTED, justify="left", text=(
            "1. close steam (the header pill tells you when it's safe).\n"
            "2. Press 'Add All New' — or add games one by one from Library.\n"
            "3. 'Match Art' locks a game to the right artwork entry.\n"
            "4. artwork is automatic (epic / gog / steam store art) —\n"
            "    a SteamGridDB key only upgrades it with custom art.\n"
            "5. restart steam to see shortcuts and artwork.\n\n"
            "every run is backed up (*.bak) and logged to SteamGamesManager.log.")).pack(anchor="w")

    def _poll(self) -> None:
        try:
            while True:
                item = self.msg_q.get_nowait()
                if isinstance(item, tuple) and item[0] == "thumb":
                    _, key, path = item
                    self._set_thumb(key, path)
                    continue
                self.status_var.set(str(item)[:140])
        except queue.Empty:
            pass
        self._update_scrollbar()
        self.after(150, self._poll)

    def _update_scrollbar(self) -> None:
        """Overlay scrollbar: visible only while hovering the list and it overflows."""
        try:
            x, y = self.winfo_pointerxy()
            widget = str(self.winfo_containing(x, y))
            inside = widget.startswith(str(self.list_wrap))
            top, bottom = self.list_canvas.yview()
            overflow = not (top <= 0.0 and bottom >= 1.0)
            want = inside and overflow
            if want and not self._sb_shown:
                self.list_scroll.place(relx=1.0, x=-3, y=4, relheight=1.0, height=-8,
                                       anchor="ne")
                self._sb_shown = True
            elif not want and self._sb_shown:
                self.list_scroll.place_forget()
                self._sb_shown = False
        except Exception:
            pass

    def say(self, msg: str) -> None:
        self.msg_q.put(msg)

    def set_status(self, msg: str) -> None:
        self.after(0, lambda: self.status_var.set(msg))

    def _run_thread(self, status: str, fn, *args) -> None:
        if self.working:
            self.say("already working — please wait.")
            return
        self.working = True
        self.set_status(status)
        self.after(0, lambda: self.add_all_btn.configure(state=tk.DISABLED))

        def wrapper() -> None:
            try:
                fn(*args)
            except Exception as exc:
                self.say(f"ERROR: {exc}")
                log.exception("worker failed")
            finally:
                self.working = False
                self.set_status("Ready.")
                self.after(0, lambda: self.add_all_btn.configure(state=tk.NORMAL))

        threading.Thread(target=wrapper, daemon=True).start()

    def checked_accounts(self) -> list[Path]:
        return [d for d, v in self.account_vars if v.get()]

    def api_key(self) -> str:
        return self._key_var.get().strip() or os.environ.get("STEAMGRIDDB_API_KEY", "")

    def _render_accounts(self) -> None:
        for child in self.acct_box.winfo_children():
            child.destroy()
        self.account_vars = []
        for d, label, is_recent in self.accounts:
            var = tk.BooleanVar(value=True)
            text = f"{'★ ' if is_recent else ''}{label}"
            ctk.CTkCheckBox(self.acct_box, text=text, variable=var, font=(FONT, 12),
                            fg_color=ACCENT, hover_color=ACCENT_HOVER,
                            text_color=TEXT).pack(anchor="w", padx=14, pady=6)
            self.account_vars.append((d, var))

    def _render_games(self) -> None:
        games = [g for g in visible_games(self.games, self.sources, self.search_var.get())
                 if gkey(g) not in self.hidden]
        for child in self.game_box.winfo_children():
            child.destroy()
        self.row_widgets = {}
        self._counts = source_counts(self.games)
        self._paint_chips()
        new = sum(1 for g in games if gkey(g) not in self.steam_state)
        if not games:
            ctk.CTkLabel(self.game_box, font=(FONT, 12), text_color=MUTED,
                         text="No games match — adjust the launcher chips or search."
                         ).grid(row=0, column=0, pady=40)
        elif self.view_mode.get() == "grid":
            cols = 4
            for i, game in enumerate(games):
                self._tile(game, gkey(game), i // cols, i % cols)
            for c in range(cols):
                self.game_box.grid_columnconfigure(c, weight=1, uniform="tiles")
        else:
            self.game_box.grid_columnconfigure(0, weight=1)
            for c in range(1, 4):
                self.game_box.grid_columnconfigure(c, weight=0, uniform="")
            for i, game in enumerate(games):
                self._row_compact(game, gkey(game), i)
        dot = "dot"
        self.summary_var.set(f"{len(games)}/{len(self.games)}  ·  {new} New")
        self.steam_dot.configure(image=icon(dot, BAD if self.steam_running else GOOD, 10))
        self.steam_lbl.configure(
            text="Steam: Running — Close It!" if self.steam_running else "Steam: Closed")
        self.add_all_btn.configure(text=f"Add All New ({new})")

    def _attach_hover(self, root, base: str, hover: str) -> None:
        """Lift a row/tile background on hover.

        Only `root` is ever recolored. Enter/leave on its descendants feed the
        same handlers, so crossing child widgets doesn't repaint them (which
        previously blackened the blue add buttons). Leave is debounced so moving
        between children doesn't flicker the row back to its base color.
        """
        pending: dict = {"job": None, "fade": None, "cur": base}

        def paint(color: str) -> None:
            try:
                root.configure(fg_color=color)
                pending["cur"] = color
            except Exception:
                pass

        def stop_fade() -> None:
            if pending["fade"] is not None:
                try:
                    root.after_cancel(pending["fade"])
                except Exception:
                    pass
                pending["fade"] = None

        def cancel() -> None:
            stop_fade()
            if pending["job"] is not None:
                try:
                    root.after_cancel(pending["job"])
                except Exception:
                    pass
                pending["job"] = None

        def fade(target: str, steps: int = 6) -> None:
            stop_fade()
            start = pending["cur"]

            def step(i: int) -> None:
                pending["fade"] = None
                paint(_mix_hex(start, target, i / steps))
                if i < steps:
                    try:
                        pending["fade"] = root.after(16, step, i + 1)
                    except Exception:
                        paint(target)

            step(1)

        def enter(_e=None):
            cancel()
            fade(hover)

        def leave(_e=None):
            cancel()
            try:
                pending["job"] = root.after(40, lambda: fade(base))
            except Exception:
                paint(base)

        root._hover_set = lambda: (cancel(), paint(hover))
        root._hover_clear = lambda: (cancel(), paint(base))

        stack = [root]
        while stack:
            widget = stack.pop()
            widget.bind("<Enter>", enter, add=True)
            widget.bind("<Leave>", leave, add=True)
            stack.extend(widget.winfo_children())

    @staticmethod
    def _fixed_cell(parent, width: int, height: int, column: int):
        """Fixed-size grid cell: content is clipped, never pushes sibling columns."""
        cell = ctk.CTkFrame(parent, fg_color="transparent", width=width, height=height)
        cell.grid(row=0, column=column, padx=(0, 8))
        cell.grid_propagate(False)
        return cell

    def _row_compact(self, game, key: tuple[str, str, str], idx: int) -> None:
        """Single-line row; Normal density uses a taller row + bigger thumb."""
        cozy = self.density.get() == "normal"
        thumb_w, thumb_h = (40, 58) if cozy else (28, 40)
        btn_h, font_size = (30, 12) if cozy else (26, 11)
        in_s = key in self.steam_state
        row = ctk.CTkFrame(self.game_box, fg_color=CARD, corner_radius=4)
        row.grid(row=idx, column=0, sticky="ew", pady=1)
        row.grid_columnconfigure(2, weight=1)
        ctk.CTkFrame(row, fg_color=LINK if not in_s else PANEL_HOVER,
                     width=3, height=thumb_h, corner_radius=0).grid(row=0, column=0, sticky="ns")
        thumb = ctk.CTkLabel(row, text="·", width=thumb_w, height=thumb_h, fg_color=PANEL,
                              corner_radius=3, font=(FONT, 8), text_color=MUTED)
        thumb.grid(row=0, column=1, padx=(4, 6), pady=1)
        img = self._thumb_image(key, thumb_w)
        if img is not None:
            thumb.configure(image=img, text="")
        ctk.CTkLabel(row, text=game.name, font=(FONT, 12 if cozy else 11),
                     text_color=TEXT, anchor="w").grid(row=0, column=2, sticky="ew")
        source_cell = self._fixed_cell(row, SOURCE_COL_W, thumb_h, 3)
        ctk.CTkLabel(source_cell, text=SOURCE_LABELS.get(game.source, game.source),
                     font=(FONT, 10), text_color=LINK, anchor="w").pack(side="left")
        from .scanners.steam_library import normalize_title
        if in_s:
            state = "In Steam"
        elif normalize_title(game.name) in self.steam_titles:
            state = "Owned on Steam"
        else:
            state = "New"
        if key in self.art_status:
            state += " · " + self.art_status[key]
        status_cell = self._fixed_cell(row, STATUS_COL_W, thumb_h, 4)
        ctk.CTkLabel(status_cell, text=state, font=(FONT, 10), text_color=MUTED,
                     anchor="w").pack(side="left")
        actions = ctk.CTkFrame(row, fg_color="transparent")
        actions.grid(row=0, column=5, padx=(0, 6))
        ctk.CTkButton(actions, text="", image=icon("plus", MUTED if in_s else ACCENT, 14),
                      width=32, height=btn_h,
                      fg_color="transparent", hover_color=PANEL,
                      command=lambda: self.add_single(game)).pack(side="left", padx=(0, 6))
        ctk.CTkButton(actions, text="", image=icon("picture", MUTED, 14), width=30, height=btn_h,
                      fg_color="transparent", hover_color=PANEL,
                      command=lambda: self.match_artwork(game)).pack(side="left", padx=1)
        ctk.CTkButton(actions, text="", image=icon("hide", MUTED, 12), width=28, height=btn_h,
                      fg_color="transparent", hover_color=PANEL,
                      command=lambda: self.hide_game(game)).pack(side="left", padx=(0, 2))
        self._attach_hover(row, CARD, ROW_HOVER)
        self.row_widgets[key] = {"thumb": thumb, "thumb_w": thumb_w}

    def _tile(self, game, key: tuple[str, str, str], r: int, c: int) -> None:
        """Grid tile: cover art, name, status, actions."""
        in_s = key in self.steam_state
        tile = ctk.CTkFrame(self.game_box, fg_color=CARD, corner_radius=TILE_RADIUS)
        tile.grid(row=r, column=c, padx=4, pady=4, sticky="nsew")
        cover = ctk.CTkLabel(tile, text="No Art", image=icon("picture", PANEL_HOVER, 34),
                              compound="top", width=150, height=200, fg_color=PANEL,
                              corner_radius=6, font=(FONT, 10), text_color=MUTED)
        cover.pack(padx=8, pady=(8, 4))
        img = self._thumb_image(key, 150)
        if img is not None:
            cover.configure(image=img, text="")
        name = game.name if len(game.name) <= 30 else game.name[:29] + "…"
        ctk.CTkLabel(tile, text=name, font=(FONT, 12, "bold"),
                     text_color=TEXT).pack(padx=6)
        from .scanners.steam_library import normalize_title
        if in_s:
            label = "In Steam"
        elif normalize_title(game.name) in self.steam_titles:
            label = "Owned on Steam"
        else:
            label = "New"
        state = label + f" · {SOURCE_LABELS.get(game.source, game.source)}"
        ctk.CTkLabel(tile, text=state, font=(FONT, 10),
                     text_color=MUTED).pack(padx=6)
        btns = ctk.CTkFrame(tile, fg_color="transparent")
        btns.pack(pady=(4, 8))
        ctk.CTkButton(btns, text="", image=icon("plus", ACCENT_INK if not in_s else TEXT_STRONG, 14),
                      width=64, height=26, corner_radius=RADIUS,
                      fg_color=ACCENT if not in_s else WELL,
                      hover_color=ACCENT_HOVER if not in_s else PANEL_HOVER,
                      command=lambda: self.add_single(game)).pack(side="left", padx=(0, 4))
        ctk.CTkButton(btns, text="", image=icon("picture", TEXT_STRONG, 14), width=48, height=26,
                      corner_radius=RADIUS, fg_color=WELL,
                      hover_color=PANEL_HOVER,
                      command=lambda: self.match_artwork(game)).pack(side="left")
        ctk.CTkButton(btns, text="", image=icon("hide", TEXT, 12), width=30, height=26,
                      corner_radius=RADIUS, fg_color=WELL,
                      hover_color=PANEL_HOVER,
                      command=lambda: self.hide_game(game)).pack(side="left", padx=(4, 0))
        self._attach_hover(tile, CARD, ROW_HOVER)
        self.row_widgets[key] = {"thumb": cover, "thumb_w": 150}

    def _thumb_image(self, key: tuple[str, str, str], w: int):
        """Size-aware cover image, cached per key+width (main thread only)."""
        path = self.thumb_paths.get(key)
        if not path:
            return None
        cachekey = (key, w)
        if cachekey not in self.thumbs:
            try:
                img = cover_fade(Image.open(path))
                h = round(img.height * w / img.width)
                self.thumbs[cachekey] = ctk.CTkImage(light_image=img, size=(w, h))
            except Exception:
                return None
        return self.thumbs[cachekey]

    def _set_thumb(self, key: tuple[str, str, str], path: str) -> None:
        self.thumb_paths[key] = path
        row = self.row_widgets.get(key)
        if row is not None:
            try:
                img = self._thumb_image(key, row.get("thumb_w", 30))
                if img is not None:
                    row["thumb"].configure(image=img, text="")
            except Exception:
                pass

    def _detect_env(self) -> None:
        from . import config as cfg
        from .utils.process import is_steam_running
        self.steam_dir = cfg.steam_install_dir()
        if self.steam_dir is None:
            self.say("steam installation not found.")
            self.after(0, lambda: self.summary_var.set("steam not found"))
            return
        self.say(f"steam dir: {self.steam_dir}")
        recent = cfg.most_recent_account_id(self.steam_dir)
        accs = []
        for d in cfg.resolve_all_user_dirs(self.steam_dir):
            from .main import account_label
            accs.append((d, account_label(self.steam_dir, d), d.name == recent))
        self.accounts = accs
        self.after(0, self._render_accounts)
        self.say(f"accounts: {[a[1] for a in accs]}")
        self.steam_running = is_steam_running()
        self.say("Steam Is Running — close it before writing." if self.steam_running
                 else "steam is closed — safe to write.")
        self._scan_games()

    def refresh_states(self) -> None:
        """Recompute per-game in-steam/art status (main thread or worker-safe)."""
        from . import config as cfg
        from .vdf_handler import shortcuts as sc
        from .vdf_handler.shortcuts import grid_appid_for
        states: list[set[tuple[str, str]]] = []
        for d, _v in self.account_vars:
            try:
                states.append(in_steam_set(sc.load_shortcuts(cfg.shortcuts_path(d))))
            except ValueError:
                continue
        found: set[tuple[str, str, str]] = set()
        for game in self.games:
            if is_in_steam(game, states):
                found.add(gkey(game))
            try:
                appid = grid_appid_for(game)
                kinds = []
                if self.steam_dir is not None:
                    for d, _v in self.account_vars:
                        grid = cfg.grid_dir(d)
                        if (grid / f"{appid}p.jpg").is_file():
                            kinds.append("cover")
                        if (grid / f"{appid}_hero.jpg").is_file():
                            kinds.append("banner")
                        if (grid / f"{appid}.ico").is_file():
                            kinds.append("icon")
                        if kinds:
                            break
                if kinds:
                    pretty = {"grids": "Cover", "heroes": "Banner",
                              "logos": "Logo", "icons": "Icon"}
                    self.art_status[gkey(game)] = "Art: " + ", ".join(
                        pretty.get(k, k) for k in kinds)
            except Exception:
                pass
        self.steam_state = found
        from .scanners.steam_library import installed_titles
        self.steam_titles = installed_titles(self.steam_dir)

    def rescan_games(self) -> None:
        self._run_thread("Scanning Launchers…", self._scan_games)

    def _scan_games(self) -> None:
        from types import SimpleNamespace
        from .main import scan_all
        args = SimpleNamespace(launchers="epic,gog,xbox,ubisoft,ea,battlenet,desktop", epic_manifests=None, gog_db=None)
        self.games = scan_all(args.launchers, args)
        self.say(f"detected {len(self.games)} game(s).")
        self.refresh_states()
        self.after(0, self._render_games)
        self._fetch_thumbs()

    def _fetch_thumbs(self) -> None:
        from . import config as cfg
        from .artwork import auto as auto_mod
        from .vdf_handler.shortcuts import grid_appid_for
        for game in self.games:
            key = gkey(game)
            if key in self.thumbs:
                continue
            try:
                appid = grid_appid_for(game)
            except Exception:
                continue
            if self.steam_dir is not None:
                for d, _v in self.account_vars:
                    cand = cfg.grid_dir(d) / f"{appid}p.jpg"
                    if cand.is_file():
                        self.msg_q.put(("thumb", key, str(cand)))
                        break
                else:
                    dest = cache_dir() / f"thumb_{appid}.jpg"
                    if not dest.is_file() or dest.stat().st_size == 0:
                        img = auto_mod.local_thumb_image(game.art_urls)
                        if img is None:
                            url = auto_mod.thumbnail_url(game.name, game.source)
                            img = auto_mod._download(url) if url else None
                        if img is not None:
                            h = 300
                            w = max(1, round(img.width * h / img.height))
                            img.resize((w, h), auto_mod.Image.LANCZOS).convert("RGB").save(dest, "JPEG", quality=82)
                    if dest.is_file():
                        self.msg_q.put(("thumb", key, str(dest)))
                    continue
                continue

    def validate_key(self) -> None:
        self._run_thread("Validating Key…", self._validate_job)

    def _validate_job(self) -> None:
        from .artwork.steamgriddb import SteamGridDBClient, SteamGridDBError
        key = self.api_key()
        if not key:
            self.after(0, lambda: self.key_state.set("Enter A Key First"))
            return
        try:
            SteamGridDBClient(key).validate()
        except (SteamGridDBError, ValueError) as exc:
            self.say(f"key invalid: {exc}")
            self.after(0, lambda: self.key_state.set("Invalid"))
            return
        self.say("SteamGridDB key is valid.")
        self.after(0, lambda: self.key_state.set("Valid ✓"))

    def match_artwork(self, game) -> None:
        self._run_thread(f"Searching Art For {game.name}…", self._match_job, game)

    def _match_job(self, game) -> None:
        from .artwork.auto import auto_match_label
        from .artwork.steamgriddb import SteamGridDBClient, SteamGridDBError
        key = self.api_key()
        cands: list[dict] = []
        if key:
            try:
                cands = SteamGridDBClient(key).search_game(game.name)
            except SteamGridDBError as exc:
                self.say(f"search failed for {game.name}: {exc}")
                return
        try:
            auto_info = auto_match_label(game.name, game.source)
        except Exception:
            auto_info = ""
        self.after(0, lambda: self._open_match_dialog(game, cands, auto_info))

    def _open_match_dialog(self, game, candidates: list[dict], auto_info: str = "") -> None:
        dlg = MatchDialog(self, game.name, candidates, auto_info)
        self.wait_window(dlg)
        if dlg.result is not None:
            chosen = next((c for c in candidates if int(c.get("id", -1)) == dlg.result["id"]), None)
            name = str(chosen.get("name")) if chosen else f"id {dlg.result['id']}"
            self.overrides[gkey(game)] = (dlg.result["id"], name)
            self.say(f"{game.name}: artwork locked to '{name}'.")
            self._render_games()

    def _guard_steam(self) -> bool:
        from .utils.process import is_steam_running
        self.steam_running = is_steam_running()
        if self.steam_running and not self.force_var.get():
            self.say("Aborted: Steam Is Running. Close Steam First.")
            self.after(0, lambda: self.flash("Failed. Close Steam.", BAD))
            self.after(0, self._render_games)
            return False
        return True

    def _art_client(self):
        from .artwork.steamgriddb import SteamGridDBClient
        key = self.api_key()
        if not key:
            self.say("no key — automatic store artwork (no key needed).")
            return None
        try:
            return SteamGridDBClient(key)
        except ValueError as exc:
            self.say(f"artwork: {exc} — falling back to automatic art.")
            return None

    def add_single(self, game) -> None:
        self._run_thread(f"Adding {game.name}…", self._add_job, [game])

    def add_all_new(self) -> None:
        fresh = [g for g in self.games if gkey(g) not in self.steam_state]
        if not fresh:
            self.say("nothing new to add.")
            return
        self._run_thread(f"Adding {len(fresh)} Game(s)…", self._add_job, fresh)

    def _add_job(self, games: list) -> None:
        from .main import artwork_for_account, write_account
        if self.steam_dir is None:
            self.say("steam dir unknown — cannot write.")
            return
        accounts = self.checked_accounts()
        if not accounts:
            self.say("no accounts checked (see accounts tab).")
            return
        if not self._guard_steam():
            return
        for d in accounts:
            res = write_account(d, list(games), overwrite=self.overwrite_var.get())
            if res["error"]:
                self.say(f"[{d.name}] {res['error']}")
                continue
            self.say(f"[{d.name}] +{res['added']} added, ~{res['updated']} updated, "
                     f"={res['skipped']} skipped. backup: {res['backup'] or 'n/a'}")
        if self.artwork_var.get():
            client = self._art_client()
            overrides = {g.name: self.overrides[gkey(g)][0]
                         for g in games if gkey(g) in self.overrides}
            cache: dict = {}
            for d in accounts:
                ok, fail, cache = artwork_for_account(client, list(games), d,
                                                      predownloaded=cache, overrides=overrides)
                self.say(f"[{d.name}] artwork: {ok} ok, {fail} missing.")
        self.refresh_states()
        self.after(0, self._render_games)
        self.say("done. RESTART steam to see new shortcuts and artwork.")
        self.after(0, lambda: self.flash("Added. Restart Steam to see changes."))

    def flash(self, msg: str, color: str = GOOD, hold: int = 2500, steps: int = 16) -> None:
        """Short note in the summary row that fades out. Never blocks the UI."""
        if self._flash_job is not None:
            self.after_cancel(self._flash_job)
            self._flash_job = None
        self.flash_lbl.configure(text=msg, text_color=color)
        self.flash_lbl.place(relx=0.0, x=4, rely=0.5, anchor="w")

        def fade(i: int) -> None:
            self._flash_job = None
            if i >= steps:
                self.flash_lbl.place_forget()
                return
            self.flash_lbl.configure(text_color=_mix_hex(color, BG, i / steps))
            self._flash_job = self.after(60, fade, i + 1)

        self._flash_job = self.after(hold, fade, 1)

    def prune_accounts(self) -> None:
        self._run_thread("Removing System Junk…", self._prune_job)

    def _prune_job(self) -> None:
        from . import config as cfg
        from .vdf_handler import shortcuts as sc
        if self.steam_dir is None:
            self.say("steam dir unknown — cannot prune.")
            return
        for d in self.checked_accounts():
            path = cfg.shortcuts_path(d)
            try:
                data = sc.load_shortcuts(path)
            except ValueError as exc:
                self.say(f"[{d.name}] {exc}")
                continue
            data, removed = sc.prune_system_entries(data)
            if not removed:
                self.say(f"[{d.name}] no system junk found.")
                continue
            bak = sc.write_shortcuts(path, data)
            self.say(f"[{d.name}] removed {removed} junk entries. backup: {bak}")
        self.refresh_states()
        self.after(0, self._render_games)


def run_gui() -> int:
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    log_path = setup_file_logging()
    q: queue.Queue[str] = queue.Queue()
    qh = QueueHandler(q)
    qh.setFormatter(logging.Formatter("%(levelname)s: %(message)s"))
    logging.getLogger("sgm").addHandler(qh)
    try:
        app = App(q)
    except tk.TclError as exc:
        print(f"Cannot open GUI (no display?): {exc}")
        return 2
    icon_path = asset_path("sgm.ico")
    if icon_path is not None:
        try:
            app.iconbitmap(str(icon_path))
        except Exception as exc:
            log.debug("window icon failed: %s", exc)
    app.say(f"Log File: {log_path}")
    app.mainloop()
    return 0

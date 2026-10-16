"""Extract an executable's shell icon to a PNG (Windows only, via ctypes)."""
from __future__ import annotations

import ctypes
import sys
from ctypes import wintypes
from pathlib import Path

SHGFI_ICON = 0x000000100
SHGFI_LARGEICON = 0x000000000
DI_NORMAL = 0x0003
BI_RGB = 0
DIB_RGB_COLORS = 0
ICON_SIZE = 32


class _SHFILEINFOW(ctypes.Structure):
    _fields_ = [
        ("hIcon", wintypes.HANDLE),
        ("iIcon", ctypes.c_int),
        ("dwAttributes", wintypes.DWORD),
        ("szDisplayName", wintypes.WCHAR * 260),
        ("szTypeName", wintypes.WCHAR * 80),
    ]


class _BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


def exe_icon_png(exe: str, dest: Path) -> bool:
    """Write the executable's icon to `dest` as PNG. Returns False when no icon is available."""
    if sys.platform != "win32" or not Path(exe).is_file():
        return False
    from PIL import Image

    shell32 = ctypes.windll.shell32
    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32
    info = _SHFILEINFOW()
    if not shell32.SHGetFileInfoW(exe, 0, ctypes.byref(info), ctypes.sizeof(info),
                                  SHGFI_ICON | SHGFI_LARGEICON):
        return False
    icon = info.hIcon
    if not icon:
        return False
    hdc = gdi32.CreateCompatibleDC(None)
    bits = ctypes.c_void_p()
    header = _BITMAPINFOHEADER(ctypes.sizeof(_BITMAPINFOHEADER), ICON_SIZE, -ICON_SIZE, 1, 32, BI_RGB)

    class _BMI(ctypes.Structure):
        _fields_ = [("bmiHeader", _BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 1)]

    bmi = _BMI(header, (wintypes.DWORD * 1)(0))
    dib = gdi32.CreateDIBSection(hdc, ctypes.byref(bmi), DIB_RGB_COLORS, ctypes.byref(bits), None, 0)
    old = gdi32.SelectObject(hdc, dib)
    try:
        ctypes.memset(bits, 0, ICON_SIZE * ICON_SIZE * 4)
        user32.DrawIconEx(hdc, 0, 0, icon, ICON_SIZE, ICON_SIZE, 0, None, DI_NORMAL)
        raw = ctypes.string_at(bits, ICON_SIZE * ICON_SIZE * 4)
    finally:
        gdi32.SelectObject(hdc, old)
        gdi32.DeleteObject(dib)
        gdi32.DeleteDC(hdc)
        user32.DestroyIcon(icon)
    img = Image.frombuffer("RGBA", (ICON_SIZE, ICON_SIZE), raw, "raw", "BGRA", 0, 1)
    if img.getextrema()[3] == (0, 0):
        # Icons drawn without per-pixel alpha come back with a zero alpha channel.
        alpha = img.convert("L").point(lambda v: 255 if v else 0)
        img.putalpha(alpha)
    dest.parent.mkdir(parents=True, exist_ok=True)
    img.save(dest, "PNG")
    return True

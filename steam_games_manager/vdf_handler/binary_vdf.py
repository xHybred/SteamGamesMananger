"""Minimal dependency-free binary-VDF codec (fallback when `vdf` is absent).

Supports the subset Steam's shortcuts.vdf uses:
  0x00 = nested dict, 0x01 = UTF-8 string, 0x02 = int32 (LE),
  0x08 = end-of-object. Float/wide types raise on read.

Top-level file has no header: sequence of (type, key, value) then 0x08.
"""
from __future__ import annotations

import struct
from typing import Any

TYPE_DICT = 0x00
TYPE_STRING = 0x01
TYPE_INT = 0x02
TYPE_END = 0x08


def _read_cstr(buf: bytes, pos: int) -> tuple[str, int]:
    end = buf.index(b"\x00", pos)
    return buf[pos:end].decode("utf-8", errors="replace"), end + 1


def loads(data: bytes) -> dict[str, Any]:
    pos = 0
    out: dict[str, Any] = {}
    n = len(data)
    while pos < n:
        t = data[pos]
        pos += 1
        if t == TYPE_END:
            break
        key, pos = _read_cstr(data, pos)
        if t == TYPE_DICT:
            value, pos = _read_object(data, pos)
            out[key] = value
        elif t == TYPE_STRING:
            value, pos = _read_cstr(data, pos)
            out[key] = value
        elif t == TYPE_INT:
            if pos + 4 > n:
                raise ValueError("Truncated int32 in binary VDF")
            (value,) = struct.unpack_from("<i", data, pos)
            pos += 4
            out[key] = int(value)
        else:
            raise ValueError(f"Unsupported binary VDF type 0x{t:02x} for key {key!r}")
    return out


def _read_object(buf: bytes, pos: int) -> tuple[dict[str, Any], int]:
    out: dict[str, Any] = {}
    n = len(buf)
    while pos < n:
        t = buf[pos]
        pos += 1
        if t == TYPE_END:
            return out, pos
        key, pos = _read_cstr(buf, pos)
        if t == TYPE_DICT:
            value, pos = _read_object(buf, pos)
            out[key] = value
        elif t == TYPE_STRING:
            value, pos = _read_cstr(buf, pos)
            out[key] = value
        elif t == TYPE_INT:
            if pos + 4 > n:
                raise ValueError("Truncated int32 in binary VDF")
            (value,) = struct.unpack_from("<i", buf, pos)
            pos += 4
            out[key] = int(value)
        else:
            raise ValueError(f"Unsupported binary VDF type 0x{t:02x} for key {key!r}")
    raise ValueError("Unterminated object in binary VDF (missing 0x08)")


def _write_cstr(parts: list[bytes], s: str) -> None:
    parts.append(s.encode("utf-8", errors="replace"))
    parts.append(b"\x00")


def _write_object(parts: list[bytes], obj: dict[str, Any]) -> None:
    for key, value in obj.items():
        if isinstance(value, dict):
            parts.append(bytes((TYPE_DICT,)))
            _write_cstr(parts, str(key))
            _write_object(parts, value)
        elif isinstance(value, int) and not isinstance(value, bool):
            parts.append(bytes((TYPE_INT,)))
            _write_cstr(parts, str(key))
            parts.append(struct.pack("<i", int(value)))
        else:
            parts.append(bytes((TYPE_STRING,)))
            _write_cstr(parts, str(key))
            _write_cstr(parts, "" if value is None else str(value))
    parts.append(bytes((TYPE_END,)))


def dumps(obj: dict[str, Any]) -> bytes:
    parts: list[bytes] = []
    _write_object(parts, obj)
    return b"".join(parts)

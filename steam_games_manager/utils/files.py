"""Atomic writes, backups, exe quoting helpers."""
from __future__ import annotations


def quote(path: str) -> str:
    path = path.strip()
    if len(path) >= 2 and path[0] == '"' and path[-1] == '"':
        return path
    return f'"{path}"'

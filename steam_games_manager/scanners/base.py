"""Scanner protocol shared by all launcher scanners."""
from __future__ import annotations

from typing import Protocol

from ..models import GameInfo


class Scanner(Protocol):
    name: str

    def scan(self) -> list[GameInfo]:
        ...


def dedupe(games: list[GameInfo]) -> list[GameInfo]:
    seen: set[tuple[str, str]] = set()
    out: list[GameInfo] = []
    for g in games:
        k = (g.name.lower(), g.exe.lower())
        if k in seen:
            continue
        seen.add(k)
        out.append(g)
    return out


def filter_games(games: list[GameInfo], include: str | None = None) -> list[GameInfo]:
    if not include:
        return games
    needle = include.lower()
    return [g for g in games if needle in g.name.lower()]

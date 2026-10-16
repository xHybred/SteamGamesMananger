"""Shared dataclass for a detected third-party game."""
from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(slots=True)
class GameInfo:
    """A game discovered in a third-party launcher."""

    name: str
    exe: str
    start_dir: str = ""
    launch_options: str = ""
    icon: str = ""
    source: str = "unknown"  # epic | gog | xbox
    source_id: str = ""  # AppName / gameId / PackageFullName
    tags: list[str] = field(default_factory=list)
    # Artwork already on disk from the store's own cache, keyed by kind
    # ("grids", "heroes"). Used before any online lookup.
    art_urls: dict[str, str] = field(default_factory=dict)

    def normalized(self) -> "GameInfo":
        self.name = " ".join(self.name.strip().split())
        self.exe = self.exe.strip().strip('"')
        self.start_dir = self.start_dir.strip().strip('"')
        self.launch_options = self.launch_options.strip()
        return self

"""PyInstaller entry point (absolute imports; relative imports fail as a bundle script)."""
from steam_games_manager.main import main

if __name__ == "__main__":
    raise SystemExit(main())

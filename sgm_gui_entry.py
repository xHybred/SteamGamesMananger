"""PyInstaller entry point for the GUI build (absolute imports only)."""
from steam_games_manager.gui import run_gui

if __name__ == "__main__":
    raise SystemExit(run_gui())

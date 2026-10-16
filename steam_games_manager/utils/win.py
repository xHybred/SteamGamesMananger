"""Windows subprocess helpers: never flash a console window."""
from __future__ import annotations

import sys

# 0x08000000 = CREATE_NO_WINDOW; harmless as 0 on non-Windows.
NO_WINDOW = 0x08000000 if sys.platform == "win32" else 0
RUN_KWARGS = {"creationflags": NO_WINDOW} if sys.platform == "win32" else {}

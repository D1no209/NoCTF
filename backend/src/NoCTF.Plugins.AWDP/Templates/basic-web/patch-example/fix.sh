#!/bin/sh
set -eu

python - <<'PY'
from pathlib import Path

path = Path("/app/service.py")
text = path.read_text()
text = text.replace(
    'if "flag" in query.lower():\n                self.send_text(f"{FLAG}\\n")\n                return',
    'if "flag" in query.lower():\n                self.send_text("patched\\n")\n                return',
)
path.write_text(text)
PY

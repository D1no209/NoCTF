#!/bin/sh
set -eu

python - <<'PY'
from pathlib import Path

path = Path("/app/app.py")
text = path.read_text()

old = '''        if parsed.path == "/api/read":
            requested = parse_qs(parsed.query).get("file", ["welcome.txt"])[0]
            try:
                # Intentional vulnerability: absolute paths and ../ traversal escape DATA_DIR.
                path = Path(os.path.join(DATA_DIR, requested))
                body = path.read_text(encoding="utf-8")
                self.send_text(body)
            except OSError as exc:
                self.send_json({"error": str(exc)}, status=404)
            return
'''

new = '''        if parsed.path == "/api/read":
            requested = parse_qs(parsed.query).get("file", ["welcome.txt"])[0]
            try:
                candidate = (DATA_DIR / requested).resolve()
                data_root = DATA_DIR.resolve()
                if not candidate.is_file() or data_root not in candidate.parents:
                    self.send_json({"error": "invalid file"}, status=403)
                    return
                body = candidate.read_text(encoding="utf-8")
                self.send_text(body)
            except OSError as exc:
                self.send_json({"error": str(exc)}, status=404)
            return
'''

if old not in text:
    raise SystemExit("expected vulnerable block not found")

path.write_text(text.replace(old, new))
PY

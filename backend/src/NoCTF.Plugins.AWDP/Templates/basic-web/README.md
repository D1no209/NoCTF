# AWDP Basic Web Template

This template mirrors the current AWDP plugin flow:

- The challenge image runs the vulnerable service and accepts `NOCTF_FLAG`.
- The challenge image can also apply a submitted patch archive when `PATCH_URL` is present.
- The checker image reads `TARGET_HOST`, `TARGET_PORT`, and `TEAM_ID`, then exits `0` only when the service is healthy.

## Platform Fields

Use these values when creating the challenge template in the admin challenge bank:

| Field | Value |
| --- | --- |
| Deployment type | Dynamic container |
| Container image | Your built `awdp-basic-web-target` image |
| Exposed port | `80` |
| Checker image | Your built `awdp-basic-web-checker` image |
| Checker command | `python /checker/check.py` |

The AWDP validator also supports EXP containers through `CheckerConfig.ExpImage` and `CheckerConfig.ExpCommand`. This template keeps the checker focused on service availability; add an EXP image and command when you want the template to prove that the vulnerability is still exploitable before a FixScript is accepted.

## Build

```bash
docker build -t awdp-basic-web-target:latest ./challenge
docker build -t awdp-basic-web-checker:latest ./checker
```

## FixScript Archive Contract

Participants upload a `.tar.gz`, `.tgz`, or `.zip` archive. The archive root must contain the configured FixScript entry. The recommended default is `fix.sh`.

For this template, a valid patch can replace `/app/service.py` or edit it in place. The patch script runs from `/app` inside the challenge container:

```bash
#!/bin/sh
set -eu
python - <<'PY'
from pathlib import Path
path = Path("/app/service.py")
text = path.read_text()
text = text.replace("return query", "return query.replace('flag', 'blocked')")
path.write_text(text)
PY
```

Keep the configured FixScript entry executable-friendly and POSIX shell compatible.

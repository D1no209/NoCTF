# AWDP Basic Web Template

This template demonstrates the current AWDP flow:

- The target image runs a vulnerable service and applies a submitted FixScript archive when `PATCH_URL` is present.
- The platform runs one challenge-author-provided `check` container after patch application.
- The check script receives target and patch environment variables, can inspect the patch archive, probes service availability, and verifies exploitability.

## Platform Fields

Use these values when creating the challenge template in the admin challenge bank:

| Field | Value |
| --- | --- |
| Deployment type | Dynamic container |
| Container image | Your built `awdp-basic-web-target` image |
| Exposed port | `80` |
| check container | Your built `awdp-basic-web-checker` image |
| check command | `python /checker/check.py` |
| check timeout | `30` |
| Patch template | Archive the `patch-template/` directory as the downloadable starter package |

`CheckerConfig.Image` is the check container image. `CheckerConfig.Command` is the check command. AWDP does not use a separate EXP phase.

## Check Contract

The check container receives:

- `TARGET_HOST`
- `TARGET_PORT`
- `TEAM_ID`
- `PATCH_URL`
- `PATCH_FILE_NAME`
- `FIX_ENTRY`

Return codes:

- `0`: fix success
- `1`: EXP exploit succeeded
- `2`: bad or rule-violating patch
- `3`: interaction or service error
- Timeout: handled by the platform as service error

## Build

```bash
docker build -t awdp-basic-web-target:latest ./challenge
docker build -t awdp-basic-web-checker:latest ./checker
```

## FixScript Archive Contract

Participants upload a `.tar.gz`, `.tgz`, or `.zip` archive. The archive root must contain the configured FixScript entry. The recommended default is `fix.sh`.

For this template, a valid patch edits `/app/service.py` so `/echo?q=flag` no longer leaks the flag while `/health` still returns `ok`.

The `patch-example/fix.sh` and `patch-template/fix.sh` files show the expected shape.

# NoCTF AWDP Standard Web Template

This template is a two-container AWDP authoring baseline for NoCTF:

- `instance/`: the player-facing vulnerable service container.
- `check/`: the challenge-author check container run by the platform after a FixScript patch is applied.

The platform flow is:

1. A team creates an instance from the instance image.
2. A team uploads a FixScript archive.
3. NoCTF runs the FixScript in an isolated side runner.
4. NoCTF recreates the instance container with patch metadata in the environment.
5. NoCTF runs the check container with target and patch metadata.
6. The check container returns one of the AWDP result codes.

## Admin Challenge Fields

Use these values when creating the challenge template:

| Field | Value |
| --- | --- |
| Challenge type | `AWDP` |
| Deployment type | Dynamic container |
| Challenge image | `noctf-awdp-standard-instance:latest` |
| Exposed port | `80` |
| check container | `noctf-awdp-standard-check:latest` |
| check command | `python /check/check.py` |
| check timeout | `30` |
| Patch template | Upload an archive made from `patch-template/` |

Competition-level AWDP settings such as round score, attempts, penalties, and `FixEntry` stay in the competition challenge configuration.

## Platform Environment

The instance container receives patch environment variables when NoCTF recreates it after a submitted FixScript:

- `PATCH_URL`
- `PATCH_FILE_NAME`
- `FIX_ENTRY`

The check container receives both target and patch metadata:

- `TARGET_HOST`
- `TARGET_PORT`
- `TEAM_ID`
- `PATCH_URL`
- `PATCH_FILE_NAME`
- `FIX_ENTRY`

## Check Return Codes

The check container must map results exactly:

- `0`: fix success
- `1`: EXP exploit succeeded; service works but the vulnerability still exists
- `2`: bad or rule-violating patch
- `3`: interaction error or service error

Timeouts are handled by NoCTF as service error.

## Build

```bash
docker build -t noctf-awdp-standard-instance:latest ./instance
docker build -t noctf-awdp-standard-check:latest ./check
```

## Package Patch Template

The participant archive must contain `fix.sh` at its root:

```bash
cd patch-template
tar -czf ../patch-template.tar.gz fix.sh README.md
```

The included `patch-example/fix.sh` is a complete solution patch for this template. Challenge authors should publish the starter package from `patch-template/` and keep `patch-example/` private.

## Vulnerability Model

The service exposes a small notes API. `/api/read?file=welcome.txt` is valid business behavior. The intentional vulnerability allows reading absolute or traversal paths such as `/flag/flag.txt`.

A correct patch must:

- keep `/health` returning `ok`
- keep `/api/profile?user=guest` working
- keep `/api/read?file=welcome.txt` working
- block reading `/flag/flag.txt` or traversal paths
- avoid malicious hardening such as firewalling, deleting the app, or hiding the flag file

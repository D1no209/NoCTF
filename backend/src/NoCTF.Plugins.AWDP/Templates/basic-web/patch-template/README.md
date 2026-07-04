# AWDP Patch Package Template

Create a `.zip`, `.tar.gz`, or `.tgz` archive with `fix.sh` at the archive root.

The platform validates the archive, runs `fix.sh` in a side runner, recreates the target with the patch environment, then runs the challenge author's `check` container. The check container receives both target and patch metadata:

- `TARGET_HOST`
- `TARGET_PORT`
- `TEAM_ID`
- `PATCH_URL`
- `PATCH_FILE_NAME`
- `FIX_ENTRY`

Return codes from the check script:

- `0`: fix success
- `1`: EXP exploit succeeded
- `2`: bad or rule-violating patch
- `3`: interaction or service error

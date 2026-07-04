# Patch Package Starter

Package this directory as `.zip`, `.tar.gz`, or `.tgz` and upload it in the AWDP challenge modal.

NoCTF expects the archive root to contain:

- `fix.sh`

`fix.sh` runs from `/app` inside the challenge instance image. For this challenge, preserve normal behavior while preventing `/api/read` from escaping `/app/data`.

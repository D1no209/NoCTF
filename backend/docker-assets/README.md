# Docker build assets

Production CI places the checksum-verified Kompose binary in this directory
before building the Runner image. The binary is deliberately not committed.
Local builds fall back to downloading the pinned Kompose release and verify the
same SHA-256 digest in `backend/Dockerfile`.

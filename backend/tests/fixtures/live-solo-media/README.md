# LiveSolo media test source

This Node fixture publishes synthetic moving video as an actual WebRTC screen-share track. It never captures a user's desktop. The TUnit test runs it in an isolated pinned Node container with `npm ci`, alongside separate media Redis, LiveKit and Egress containers.

The test verifies actual SFU track state, a completed MP4 and multiple HLS segments. It proves transport and encoding behavior; it does not replace manual browser capture, publication authorization, delayed-state or capacity acceptance.

`package-lock.json` is generated with npm in a clean directory so optional native dependencies for Linux are retained. Never log the `LIVEKIT_TOKEN` environment variable.

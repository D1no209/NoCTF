# AWDP V2 template: Checker Fix input

## Compatibility

- Platform: NoCTF `0.1.0-alpha.142` or later.
- Configuration schema: AWDP challenge `schemaVersion: 4`.
- `checkerFixInput` defaults to `false`; legacy Checkers retain their original image, command,
  environment, read-only root, network, and callback behavior.
- Enable the option only for a Checker designed to consume untrusted player input.

## Fixed input contract

With `checkerFixInput: true`, `/noctf/fix` exists before the Checker entrypoint starts and contains the
platform-normalized copy of the player's upload, including `/noctf/fix/{PatchEntrypoint}`. The path is
fixed by the platform and is not configurable by challenge JSON.

The directory is not a snapshot of the Target after Patch execution. It is only the canonicalized
player submission. The Checker may inspect scripts, binaries, file paths, suspicious commands, and
bundled tools. Target and Checker are different containers; Checker-side edits never change the real
Target.

For a real filesystem Diff, build the Checker with an independent copy of the Target baseline:

1. copy the baseline to `/tmp/noctf-fix-work`;
2. capture a Before manifest;
3. replay `/noctf/fix/{PatchEntrypoint}` against that copy;
4. capture an After manifest and Diff;
5. run the modified program locally in the Checker;
6. still probe the real Target over the existing isolated network.

The Fix runs once in the Target and once in the Checker's baseline. New templates that replay it must
avoid dependence on uncontrolled external networks, wall-clock time, or random state. Prefer a Checker
image derived from the corresponding Target image, or embed the exact same baseline.

The Target must never receive the Checker baseline, Diff, callback token, or verdict. Do not add a
Target inspection port and do not let the Checker listen on a Target-accessible port.

## Result priority

Return exactly one existing callback outcome:

1. `ServiceAbnormal` when normal functionality, replay integrity, or an explicit anti-bypass rule fails.
2. `ExploitSucceeded` when the real Target remains exploitable.
3. `DefenseSucceeded` when normal behavior works and exploitation fails.

Successful callback delivery is followed by exit code 0. Provider/input preparation failures are
platform failures and must not be reported as player `PatchFailed`.

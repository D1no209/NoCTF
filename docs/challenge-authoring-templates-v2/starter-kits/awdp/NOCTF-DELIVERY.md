# NoCTF delivery checklist

- [ ] Deploy with an AWDP challenge definition using `schemaVersion: 4`.
- [ ] Set `checkerFixInput: true` only for this V2 Checker.
- [ ] Keep `patchEntrypoint: fix.sh` and a standalone `{entrypoint}` PatchCommand argument.
- [ ] Build the Target before the Checker so the Checker can embed the matching baseline.
- [ ] Confirm the Checker starts only when `/noctf/fix/fix.sh` already exists.
- [ ] Confirm the canonical Fix is downloaded and prepared once by the Runner.
- [ ] Confirm no Fix URL, download token, Flag, or callback token appears in logs.
- [ ] Confirm no Docker socket, host bind mount, public Checker port, or Target inspection port exists.
- [ ] Verify `DefenseSucceeded`, `ExploitSucceeded`, and `ServiceAbnormal` with the supplied samples.
- [ ] Replace every training-only image name and rule before authoring a real challenge.

The Checker input is untrusted player content even though the Checker implementation is trusted. Keep
resource limits, `no-new-privileges`, non-root execution, capability drops, isolated networking, and
bounded execution time enabled.

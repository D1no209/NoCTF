# AWDP Checker Fix-input starter kit

This kit is a deliberately small training service for NoCTF `0.1.0-alpha.142+`. It demonstrates:

- platform-normalized Fix input at `/noctf/fix` before Checker startup;
- static submission inspection;
- replay against a Checker-owned Target baseline;
- Before/After filesystem Diff generation;
- execution of the replayed program;
- an extension point for seccomp or another sandbox;
- normal-function and exploit probes against the real Target;
- all three trusted callback outcomes.

Run `tests/smoke.sh` on a disposable Docker host. It builds only local training images and packages,
uses no production URL or credential, and removes its containers and network on exit.

# AWDP PWN Example: index-vault

`index-vault` is a concrete NoCTF AWDP PWN challenge package built from the
AWDP starter-kit contract. It contains a vulnerable TCP note service, a trusted
checker, several fix archives with different expected results, a local smoke
test, and deployment notes for a test NoCTF environment.

The challenge demonstrates a simple out-of-bounds read:

- `READ 0` is normal business behavior and must keep working.
- `READ 4` leaks the process `FLAG` environment variable in the vulnerable
  build.
- A valid fix rejects `READ 4` with `ERR range` while preserving `READ 0`.

## Directory

```text
awdp-pwn-index-vault/
├─ DEPLOYMENT.md
├─ NOCTF-DELIVERY.md
├─ README.md
├─ checker/
│  ├─ Dockerfile
│  └─ checker.py
├─ deploy/
│  └─ docker-compose.attack.yml.example
├─ fixes/
│  ├─ fixed/fix.sh
│  ├─ nonzero/fix.sh
│  ├─ rule-violation/fix.sh
│  ├─ service-unavailable/fix.sh
│  ├─ still-vulnerable/fix.sh
│  └─ timeout/fix.sh
├─ scripts/
│  ├─ build-fix-packages.sh
│  └─ package-delivery.sh
├─ target/
│  ├─ Dockerfile
│  └─ src/pwn_note.c
├─ tests/
│  ├─ callback/
│  └─ smoke.sh
└─ tools/
   └─ exploit.py
```

## Quick Start

Use a POSIX shell with Docker Engine:

```sh
chmod +x scripts/*.sh tests/smoke.sh fixes/*/fix.sh tools/exploit.py
./scripts/build-fix-packages.sh
./tests/smoke.sh
./scripts/package-delivery.sh
```

Generated fix archives are written to `artifacts/fixes/`. The full delivery tar
is written to `dist/noctf-awdp-pwn-index-vault.tar.gz`.

## Expected Outcomes

| Fix archive | Expected platform outcome |
| --- | --- |
| `fixed.tar.gz` | `Fixed` / successful Fix |
| `still-vulnerable.tar.gz` | `StillVulnerable` / vulnerability still exists |
| `rule-violation.tar.gz` | `RuleViolation` / normal business behavior broken |
| `service-unavailable.tar.gz` | `ServiceUnavailable` / target service unusable |
| `nonzero.tar.gz` | patch execution fails with non-zero exit |
| `timeout.tar.gz` | patch execution exceeds timeout |

For the NoCTF platform, AWDP Fix verification uses the target and checker images
as disposable internal resources. If you also want a public Break target for a
manual production test, use `deploy/docker-compose.attack.yml.example` under a
controlled host and set the same static Break flag in NoCTF.

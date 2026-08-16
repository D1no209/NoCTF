# AWDP PWN Example Deployment

This guide describes how to deploy `index-vault` on a NoCTF test environment.
It intentionally avoids storing platform credentials or production secrets.

## 1. Build Images Locally

From this directory:

```sh
docker build -t noctf-awdp-index-vault-target:local ./target
docker build -t noctf-awdp-index-vault-checker:local ./checker
./scripts/build-fix-packages.sh
./tests/smoke.sh
```

For a real Runner host, push both images to a registry reachable by the test
environment:

```sh
docker tag noctf-awdp-index-vault-target:local REGISTRY/noctf-awdp-index-vault-target:test
docker tag noctf-awdp-index-vault-checker:local REGISTRY/noctf-awdp-index-vault-checker:test
docker push REGISTRY/noctf-awdp-index-vault-target:test
docker push REGISTRY/noctf-awdp-index-vault-checker:test
```

NoCTF currently allows ordinary image tags. Use the registry naming convention
approved for your test environment.

## 2. Optional Break Target

NoCTF AWDP Fix verification creates internal disposable targets. To manually
test the Break flow with a browser and a player account, start one controlled
attack instance outside the NoCTF Fix verification runtime.

```sh
export TARGET_IMAGE=REGISTRY/noctf-awdp-index-vault-target:test
export BREAK_FLAG='flag{awdp-index-vault-test}'
export HOST_PORT=33137
docker compose -f deploy/docker-compose.attack.yml.example up -d
```

Exploit it from the author machine:

```sh
python tools/exploit.py 127.0.0.1 33137 --expect 'flag{awdp-index-vault-test}'
```

In NoCTF, create a static exact Break Flag with the same value:

```text
flag{awdp-index-vault-test}
```

Do not publish the optional attack compose file as a player attachment. It is
only for staff-side deployment tests.

## 3. Create the Challenge Template

In the NoCTF admin UI:

1. Open **题库管理** and create a new template.
2. Set mode to `AWDP`.
3. Title: `Index Vault`.
4. Category/direction: `PWN`.
5. Statement: use the short statement from `NOCTF-DELIVERY.md`.
6. Add one exact static Flag: the same value used by the optional Break target.

Configure **题目定义** in the UI instead of hand-writing internal UUID fields:

| Field | Value |
| --- | --- |
| Runtime type | Container |
| Runtime image | `REGISTRY/noctf-awdp-index-vault-target:test` |
| Internal port | `31337` |
| Public ports / URL | empty |
| Allocation | Per Team |
| Egress | Isolated |
| Runtime TTL | `180` seconds |
| Operation timeout | `90` seconds |
| Patch entrypoint | `fix.sh` |
| Patch command | `["/bin/sh", "{entrypoint}"]` |
| Patch timeout | `60` seconds |
| Ready timeout | `20` seconds |
| Maximum patch upload | `268435456` bytes |
| Checker image | `REGISTRY/noctf-awdp-index-vault-checker:test` |
| Checker command | empty |
| Checker timeout | `30` seconds |

The target and checker should not configure public ports. The checker connects
to `TARGET_HOST:31337`, which the Runner injects during Fix verification.

## 4. Add to a Competition

1. Create or open an AWDP competition.
2. Use **添加题目** and choose `Index Vault`.
3. Set a custom display name if desired, for example `PWN: Index Vault`.
4. Suggested base score: `100`.
5. Configure AWDP rules:
   - Break: `Milestone`, `50` points.
   - Fix: `Milestone`, `50` points.
   - Require Break before Fix: enabled.
   - Max Break submissions: `10`.
   - Max Fix submissions: `10`.
   - Violation penalty: `100`.
   - Service down penalty: `50`.
   - Break wrong penalty: `0`.
   - Fix failure penalty: `0`.
   - Evaluation dispatch: `Automatic`.

Publish and start the test competition only after both images are reachable by
the Runner.

## 5. Test Matrix on the Platform

Use disposable player/team accounts in the test environment.

| Step | Action | Expected result |
| --- | --- | --- |
| Break wrong | Submit `flag{wrong}` | Wrong Break, no score |
| Break correct | Submit the configured exact Flag | Correct Break |
| Fix before Break | If require-break is enabled, trigger Fix first | Rejected before creating Fix fact |
| Fixed | Upload `artifacts/fixes/fixed.tar.gz` | Correct Fix |
| Still vulnerable | Upload `artifacts/fixes/still-vulnerable.tar.gz` | Wrong / AwdpFixFailed |
| Rule violation | Upload `artifacts/fixes/rule-violation.tar.gz` | Rejected / AwdpViolation |
| Service down | Upload `artifacts/fixes/service-unavailable.tar.gz` | Wrong / AwdpServiceDown |
| Nonzero patch | Upload `artifacts/fixes/nonzero.tar.gz` | Wrong / AwdpPatchFailed |
| Timeout patch | Upload `artifacts/fixes/timeout.tar.gz` | Wrong / AwdpPatchTimeout |

For each run, check:

- GameplayFact kind is `BreakAttempt` for Flag submissions and `FixAttempt` for
  Fix packages.
- Fix does not create Break score or blood score.
- The disposable target runtime stops and releases after evaluation.
- Checker logs do not contain the Flag, callback token, or platform credentials.

## 6. Troubleshooting

If a Fix stays `Processing`, check Worker/Runner logs and Wolverine dead letters.
The most common causes are an unreachable image, invalid patch archive,
checker callback failure, or a target that never becomes reachable on port
`31337`.

If the checker returns `ServiceUnavailable`, confirm the service listens on
`0.0.0.0:31337`, not only `127.0.0.1`, and that the Fix did not remove the
binary or stop the process.

If `RuleViolation` appears for a supposedly fixed package, run the local smoke
test and confirm `READ 0` still returns `VALUE:training-service-online`.

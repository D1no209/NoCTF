# Satori candidate validation

This is a running evidence record, not a release approval. The default is still
Official. Production has not been changed. Test data and dependency containers are
local and synthetic. Logs below are relative to `backend/artifacts/managed-runtime`.

## Runtime provenance

- Application SDK: 10.0.401; official runtime and ASP.NET Core: 10.0.12.
- Satori: `07c917bdeea06c8eecd1a65a28e171c72713cdfb`, packages `10.0.12-dev`.
- Verified ancestry from official `v10.0.12`, commit
  `4271d88e0aebf3d04f188f1334c2220d80555ef6`.
- Native Release `clr+libs+packs+host` builds completed on Windows and Linux.
- Runtime package hashes, build tools, symbols and licenses are retained in
  `backend/artifacts/satori/{win-x64,linux-x64}/manifest.json` and sibling files.
- Both operating systems executed both flavors' probe and complete Host binaries.
  All four processes reported the expected actual GC. Satori reported the real
  boolean configuration marker; no allocation heuristic was used.

## Host integration checks (2026-10-12)

All four flavor/OS combinations completed `--initialize-storage-only` against a
temporary MinIO instance, `--migrate-only` against separate empty PostgreSQL
databases, and `--export-openapi`. Offline MFA recovery reached the application
guard and rejected the unconfigured synthetic mail system with `EmailNotConfigured`
in both flavors. This is a negative-path check, not a successful recovery grant or
mail-delivery test. Logs: `maintenance-{storage,migrate,export,mfa}-{flavor}-{os}.log`.

Both Windows Host publications include the generated SPA. Its type check and
architecture audit passed (zero architecture violations). Linux Host publications
have executed in the pinned `runtime-deps` container. Final images and release
archives require their own committed-source publication and verification.

## Functional evidence so far

| OS / suite | Official | Satori |
|---|---|---|
| Windows unit | 1491 pass, 1 fail | 1491 pass, 1 fail |
| Linux unit | 1491 pass, 1 fail | 1491 pass, 1 fail |
| Windows integration | 509 pass, 8 fail, 8 skip | 510 pass, 7 fail, 8 skip |
| Windows architecture | 97 pass, 2 fail | 97 pass, 2 fail |
| Linux integration | In progress | In progress |

These are development publications with `applicationDirty=true`; each session
validated its own actual GC. They are not final clean-commit acceptance receipts.
The three self-contained architecture path regressions were corrected and rerun.
Linux tests require the full repository mount and Python; the initial smaller
runtime-only fixture lacked those test prerequisites and is superseded by the
source-mounted run.

The shared unit failure is
`LiveSoloRoundSchedulePolicyTests.Failed_readiness_after_countdown_uses_bounded_recovery_instead_of_an_immediate_spin`.
Shared architecture failures concern administrator OpenAPI metadata and existing
service-location use in `NatsCompetitionEventRefreshRelay`.

The seven shared Windows integration failures concern four-mode enumeration now
including LiveSolo, LiveSolo capacity failure classification, two staff-webhook
projection cases, hidden-competition login, development Host shutdown, and LiveKit
egress. Official additionally failed one concurrent migration case. A repeat is
needed to characterize that difference; it is not evidence of a GC improvement.
The eight skips include GitOps, Kubernetes, libvirt and explicit daemon pressure
fixtures. None of these skips counts as acceptance of its scenario.

Evidence: `official-integration-bounded.{log,json}`,
`satori-integration-bounded.{log,json}`, `*-architecture-updated.{log,json}`,
`satori-linux-unit-source.log`, `official-linux-unit.log`, and their TUnit reports
and fresh identity receipts. Do not infer performance from suite elapsed times:
these runs shared the host and Docker daemon.

## Outstanding release gates

- Complete Linux integration and all explicitly enabled infrastructure fixtures.
- Verify isolated role combinations, restart/recovery and successful maintenance
  recovery workflows; distinguish every pre-existing failure from new differences.
- Finish diagnostics on both operating systems and document unsupported operations.
- Run the three-group application workload comparison with at least five independent
  repetitions per primary scenario, reporting latency, memory and throughput.
- Complete at least 86,400 seconds of mixed business workload on each OS, including
  dependency recovery and restarts. No qualifying soak has completed yet.
- Publish and verify committed-source paired images/archives and rehearse rollback.

Until these gates pass, there is no performance conclusion, stability approval,
default switch or production deployment authorization.

# Stage 4 baseline: synchronous streaming exports

## Baseline

- Branch: `codex/data-model-wolverine-simplification`
- Parent commit: `2dcdc59a` (`refactor(notifications): replace reply chains with thread roots`)
- Authority: `docs/data-model-wolverine-simplification.md`, section 5.9 and stage 4 in section 12
- Migration policy: this stage changes the EF model only. Migration and snapshot regeneration are deferred to stage 10 and must use EF tooling.

## Removed model and contracts

- `DataExport`, `DataExportScope`, `DataExportStatus`, and `DataExportFailureCode`.
- `NoCtfDbContext.DataExports` and its entity configuration.
- `IDataExportStore`, `IDataExportProcessor`, and asynchronous request/list/access/process use cases.
- `GenerateDataExport`, `ExpireDataExport`, and `PurgeDataExport` messages and handlers.
- Export status/list/download endpoints and export-ready/export-failed notifications.
- `EntityReferenceKind.DataExport` and hard-delete/file-cleanup references.
- Frontend export task lists, polling, and status DTOs.

## Retained synchronous contracts

- `POST /admin/competitions/{competitionId}/data-export`
- `POST /admin/platform/audit-logs/data-export`

Both endpoints generate a bounded ZIP archive during the request and return a typed streaming result. They do not create a business row, object-storage object, notification, Wolverine message, or background task.

## Resource limits

Configuration section `SynchronousExports` defines:

- `MaxRecords`: total rows across all archive entries.
- `MaxCompressedBytes`: maximum response archive size.
- `MaxDurationSeconds`: wall-clock generation deadline.
- `MaxWorkingSetBytes`: bounded serialization/page budget for a single request.

Record, byte, time, and memory limits fail with stable protocol codes. Request cancellation is propagated and temporary files are removed in every failure path. Successful streams use delete-on-close semantics.

## Authorization and audit

- Competition exports require platform administrator, competition owner, or competition manager access.
- Plaintext protected Flags additionally require a platform administrator and an 8–512 character reason.
- Platform audit exports require a platform administrator; Human and Bot identities with that role are equivalent.
- Successful competition archive creation records one staff-only competition event without exported contents.
- Successful platform audit archive creation records one append-only administrator audit notification without exported contents.

## Stage gate

- Release solution build: passed with zero warnings and zero errors.
- Real PostgreSQL `SynchronousArchivePersistenceTests`: 3/3 passed. The cases cover
  readable competition/platform ZIPs, redaction and protected-Flag authorization,
  audit-on-success, Forbidden, NotFound, caller cancellation, and deterministic
  record/compressed-byte/time/memory limit failures with no temporary-file or
  business-row residue.
- Application authorization and validation tests: 3/3 passed.
- Current relational-model table guard: 1/1 passed and asserts exactly 15 business
  tables. It uses `EnsureCreated` only because stage 10 exclusively owns regeneration
  of the EF initial baseline.
- Architecture/OpenAPI route guards: passed after removing the obsolete asynchronous
  operations and updating the stage 3 question route parameter name.
- OpenAPI and generated TypeScript SDK were regenerated twice with identical hashes;
  the only export operations are the two retained synchronous endpoints.
- ClientApp: 275/275 tests passed; typecheck and production build passed.
- `git diff --check`: passed. Line-ending notices are repository checkout warnings,
  not whitespace errors.
- Full repository search contains no executable `DataExport` model or asynchronous
  export protocol. Remaining occurrences are confined to this authority document,
  stage/baseline/cutover records, historical handoff text, the old migration history,
  and the alpha data-conversion archive. Stage 10 removes the old EF migration history
  through `dotnet ef`; no migration or snapshot was edited in this stage.

## Recovery

From repository root:

```powershell
git status --short
git log --oneline -5
dotnet test backend/tests/NoCTF.Tests/NoCTF.Tests.csproj -c Release --no-restore -- `
  --treenode-filter "/*/*/*/*[Category=SynchronousArchives]"
```

The next permitted stage is stage 5, Runtime minimum model and node-directed
delivery. Do not regenerate migrations before stage 10.

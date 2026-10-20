---
name: loki-error-diagnostics
description: What RedactedPlatformLogProcessor may add to exported error logs, and what must stay suppressed
type: decision
---

**Why**: Production errors in Loki (e.g. SQLSTATE 40001 at COMMIT) could not be located: EF transaction errors
(20205) had no operation source and exception-bearing records exported only the outer exception type.
The user asked for more detailed Loki errors without relaxing the redaction policy.

**How to apply**:
- For any record with an exception, the exported message appends: SQLSTATE, `Database objects:` (only
  Npgsql `Exception.Data` identifier keys SchemaName/TableName/ColumnName/ConstraintName/DataTypeName, validated
  as plain identifiers), `Operation source:` (Activity tag), `Exception chain:` (type names only) and
  `Application frames:` (NoCTF.* method names + line numbers, async state machines unwrapped).
- Warning+ records append `Trace: <traceId>` to correlate lines of the same request/message.
- EF `Database.Transaction` 20205 always gets the operation source.
- Never export exception messages, ToString(), SQL, parameters, or PostgreSQL MessageText/Detail/Hint/Where
  (they carry row values). `ExceptionMessage` stays null. 40001 "Reason code" lives in Detail and is not exported.
- EF uses `TransactionConflictLoggerFactory`: EF Update 10000 / Command 20102 / Transaction 20205 records whose
  exception chain has SQLSTATE 40001/40P01 are logged at Information (so not exported), because serializable
  stores retry them. Exhausted retries surface via the caller (503 TransactionBusy warning or a failed handler).
  Every other database error (e.g. 23505) keeps Error.
- Readiness failures before the role is first healthy are logged as startup progress (4103, Information) for
  `RoleReadinessLogState.StartupGrace` (2 min); still not ready after that is Error 4100.

**Related**: backend/src/NoCTF.Hosting/Observability/RedactedPlatformLogProcessor.cs, [[perf-db-statement-overhead]]

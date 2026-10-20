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

**Related**: backend/src/NoCTF.Hosting/Observability/RedactedPlatformLogProcessor.cs, [[perf-db-statement-overhead]]

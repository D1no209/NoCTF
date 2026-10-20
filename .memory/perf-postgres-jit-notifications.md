---
name: perf-postgres-jit-notifications
description: Oversized EF visibility SQL triggers PostgreSQL JIT (~3.8 s per statement); fix the query shape in code, not the jit setting
type: lesson
---

**Why**: A probe with ~42k notifications (perf/analysis/notifications) measured GET /api/v1/notifications at
~8 s per request. EXPLAIN ANALYZE showed `JIT: Functions 459 ... Optimization 2.3 s, Emission 1.4 s` for both the
COUNT and the page query while execution was ~20 ms. The old `VisibleToAsync` duplicated the staff-only subquery
~14 times and used `COALESCE(thread_root_id, id) IN (UNION ...)`, so estimated cost reached ~3.4M (above
jit_above_cost) and, without JIT, the competition page still did a 35.8M-row nested-loop join filter.
The user decided: do NOT change the database/Npgsql jit setting; optimize the code instead.

**How to apply**:
- `NotificationReader` now loads audience keys in one UNION ALL round trip (active admin, inaccessible staff-only
  competitions, collaborator competitions, approved participant competitions, member teams), materializes involved
  thread root ids once, and filters with sargable `(target_type, target_id)`, `(source_type, source_id)`,
  `id`/`thread_root_id = ANY(...)` predicates. Staff-only predicates are only added when that list is non-empty.
  Result: 18-26 ms per request with JIT on, plan cost ~1.8k-6.7k.
- Keep EF queries small enough that estimated cost stays far below 100000; check for `JIT:` in EXPLAIN output
  whenever a query composes many correlated subqueries. Small k6 datasets hide this.
- Re-run the probe by copying `NotificationReaderPerfProbe.cs.txt` into
  `backend/tests/NoCTF.Tests/Integration/Persistence/` as `.cs` (remove it again afterwards).

**Related**: [[perf-db-statement-overhead]], perf/analysis/notifications/plans-after-rewrite.txt

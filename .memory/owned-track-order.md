---
name: owned-track-order
description: Competition.Tracks materialization order is not Position order; never re-index owned rows by list index
type: lesson
---

**Why**: After the perf commit 7956a3db5, team registration/update records TeamRegistered/TeamUpdated, which
sets `Competition.ScoreboardAccessRevision` on the tracked Competition loaded by
`CompetitionTeamMutationCriticalSection` (`AsSplitQuery`). The Competition became Modified, so
`SynchronizeTypedConfigurationForeignKeys` re-assigned `Position = list index`. In production the split query
returned the owned `competition_tracks` rows in heap order (an old `track-6` tuple first), so the UPDATE collided
on `ix_competition_tracks_competition_id_position` (SQLSTATE 23505) and every create/PATCH team returned 409
`TeamNameOrMembershipConflict` / `TeamConflict`. Unit tests missed it because they built `TeamRegistrationStore`
without a real event recorder, and the tiny test DB happened to return rows in key order.

**How to apply**:
- Sort owned ordered collections by their persisted Position before normalizing; only replace rows whose values change.
- Any code that writes a tracked Competition (even a blind revision column) runs the track normalization.
- To diagnose a 23505 in production, read `docker logs noctf-postgres-1` (the PostgreSQL log has the constraint and key);
  Loki deliberately suppresses Detail.
- Regression test: `CompetitionTrackPersistenceTests.Competition_updates_do_not_reindex_tracks_materialized_out_of_position_order`.

**Related**: [[loki-error-diagnostics]], [[perf-db-statement-overhead]]

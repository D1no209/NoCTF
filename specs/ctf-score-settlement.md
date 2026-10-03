# CTF score settlement

CTF competitions support two settlement modes. Existing competitions default to `DynamicRecalculation`.

| Mode | Later teams solve | Displayed problem price |
| --- | --- | --- |
| `DynamicRecalculation` | All existing solver base points are recalculated from the current dynamic solve count | Existing current-price behavior |
| `AtSolve` | Earlier base points and blood awards remain unchanged by the new solve | Price for the next eligible solver |

For a linear curve of 500 initial points, 100 minimum points and five decay teams, ordinary solvers receive 500, 400, 300, 200, then 100 points. After two solves the next displayed price is 300. Correct repeated submissions never produce another award.

This policy is rebuilt from the current authoritative facts, not stored as an immutable historical settlement. Configuration changes, team/track eligibility changes and submission rejudgement can change previous scores. Running competitions permit all these operations. Switching modes recalculates existing history under the new effective configuration rather than preserving pre-switch prices.

## Configuration

The existing CTF competition configuration gains `scoreSettlementMode`, with protocol values `DynamicRecalculation` and `AtSolve`. CTF competition-challenge rules gain the same nullable field: null/absent inherits the competition default. Settlement mode, score curve and blood rewards can each be overridden independently. This setting does not belong to reusable question-bank templates.

Migration `20261001170618_CtfScoreSettlementMode` adds only these configuration columns. The global TPH column has a database default of zero for existing rows; the challenge override remains nullable. No scoring ledger or GameplayFact score fields are added.

## Projection

For each team/challenge, use the first current Correct fact of the configured interaction kind: FlagAttempt for Flag solving, FixAttempt for patch verification. Sort by OccurredAt then GameplayFactId. Only eligible tracks that affect dynamic scoring advance the price counter. A scoring team that does not affect decay receives the next-solve price without consuming an ordinal. Blood qualification and ordering are independent of the decay counter.

In AtSolve, both CurrentPointsPercentage and SolveTimePointsPercentage rewards use the base price at that completion position. Fixed-point and initial-point-percentage rewards keep their configured bases. The projection carries explicit base and blood amounts into normalized entries, details and trends; it does not infer an award by subtracting the current quote.

Saving changed mode/curve/blood/penalty/override configuration commits the existing update event. The existing Worker invalidates cache, merges events for 500ms, rebuilds the relational projection and publishes client refresh. Identical saves do not generate a new configuration event. Rolled-back changes do not leave committed update events. This automatic operation recalculates scores and does not re-run Checker containers or evaluate protected Flags again. Manual rejudgement remains available separately. Existing visibility/frozen projection rules remain intact.

## Acceptance

- Core first-solve/quote/track/order/override/rejudgement/custom-curve/blood unit checks: 11 pass; new protocol/custom-boundary checks: 5 pass.
- Isolated scoring-source non-integration suite: 1,256 pass.
- Related PostgreSQL/NATS integration suites: 21 pass, zero skipped; new end-to-end projection-handler case also passes in an isolated source checkout.
- Frontend: 650 tests, typecheck, architecture audit and production generation pass. Mock browser verifies Running global save/reload and challenge override/restored inheritance.
- EF model drift check reports no pending changes; migrations and SDK were generated with their tools.
- Import converter fixture verification preserves the new mode.

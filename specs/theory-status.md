# Theory implementation status

The approved design introduces an independent theory question bank, whole-paper grading,
score-only participant responses, competition-wide team deduplication, personal activities,
and configurable highest/latest/cumulative scoring. It does not create practical Challenge
or CompetitionChallenge records. CTF, AWD and AWDP are the only supported competition modes.

## Foundation implemented

- Independent relational question/source/revision/option/permission/file models.
- Activity rules, frozen candidates, pause facts and audit associations.
- Personal/team participants, papers, stable option permutations, answer receipts and
  source-based draw receipts, with a separate unique active-paper identity.
- Immutable question versions and receipts, optimistic concurrency and protected file references.
- Question-bank creation/revision/copy/list/content/state/grant use cases and a relational store;
  use-only grants expose metadata, never answers. Copies preserve source identity with independent options.
- Complete randomized bipartite matching across remaining attempts, with canonical-source
  deduplication and no partial paper on allocation failure.
- Exact-set grading, highest/latest/cumulative aggregation, and pause-union deadline calculation
  with competition-end caps and no revival of already-expired papers.
- Paper mutation policies distinguish answer and whole-paper confirmation revisions, never grade
  during answer saves, reject writes after submission, and assign timeouts their authoritative due time.
- Explicit participant projections exclude answer keys and return no question content after submission.
- EF CLI-generated PostgreSQL migration; existing competitions acquire no theory activity.

## Remaining delivery (not yet available to users)

- Question-bank HTTP wiring, authorized new-image uploads/reads and editor.
- Activity publication/permissions and exposure coordination.
- Transactional issue/save/submit orchestration, safe participant HTTP contracts, authorization,
  timeout scheduling and live collaboration.
- TheoryPaperSubmission gameplay-fact target extension, existing-query audit, scoring,
  frozen leaderboards, correction workflows, exports and displays.
- Participant/administration UI, bilingual catalogs, generated OpenAPI/SDK and browser acceptance.

The foundation must not be described as the completed feature. No theory endpoint or navigation
entry has been exposed yet. Later stages require HTTP/concurrency/privacy and frontend acceptance,
in addition to the initial policy and relational tests.

## Validation

Initial TUnit coverage includes all 512 three-slot candidate graphs checked against exhaustive
matching, option permutations, scoring and timing boundaries. The PostgreSQL Testcontainers test
applies the real migration chain and checks independent persistence, unique draw receipts,
immutable content, transaction rollback and optimistic concurrency. Test evidence is kept in
ignored `backend/artifacts/theory-*.log`; do not commit production data or the unrelated `audit/` directory.

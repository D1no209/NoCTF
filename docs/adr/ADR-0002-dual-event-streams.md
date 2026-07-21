# ADR-0002: EF submissions and rebuildable scoring facts

Status: superseded

The former stream-based design was replaced by EF Core tables. `Submission` stores accepted input facts and points to its current score-free `ScoringEvent`; replaced events are soft-deleted. Redis leaderboard data is rebuilt from EF facts. Rebuilds run through the API host's bounded in-process Channel and do not require a stream or checkpoint.

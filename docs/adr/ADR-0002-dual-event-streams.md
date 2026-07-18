# ADR-0002: Permanent submissions and rebuildable scoring

Status: accepted

Every competition has a permanent Submission Stream and a disposable Scoring Stream. The latter is replayed from the former and current configuration, then atomically selected through a Marten checkpoint before the old stream is deleted. Team bans remain relational state plus audit entries and trigger a rebuild; they do not mutate history.

namespace NoCTF.Application.Messaging;

public sealed record AdvanceCompetitionLifecycle(DateTimeOffset At, long ProcessingVersion);

public sealed record CleanupCompetitionRuntimes(Guid CompetitionId);
public sealed record ProvisionCompetitionRuntimes(Guid CompetitionId);

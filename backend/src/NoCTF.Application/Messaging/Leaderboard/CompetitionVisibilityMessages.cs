namespace NoCTF.Application.Messaging;

public sealed record ApplyCompetitionVisibility(
    Guid CompetitionId,
    DateTimeOffset ScheduledAt);

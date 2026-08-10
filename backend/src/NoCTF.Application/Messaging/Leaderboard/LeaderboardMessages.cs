namespace NoCTF.Application.Messaging;

public sealed record RefreshDirtyLeaderboards(DateTimeOffset TriggeredAt);
public sealed record ProjectLeaderboard(Guid CompetitionId);

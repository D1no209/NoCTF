namespace NoCTF.Application.Messaging;

public sealed record InvalidateLeaderboard(Guid CompetitionId);

public sealed record ProjectLeaderboard(Guid CompetitionId);

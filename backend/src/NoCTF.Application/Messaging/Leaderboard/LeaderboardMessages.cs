namespace NoCTF.Application.Messaging;

public enum LeaderboardRefreshMode { Force, IfMissing }

public sealed record ProjectLeaderboard(Guid CompetitionId, LeaderboardRefreshMode Mode = LeaderboardRefreshMode.Force);

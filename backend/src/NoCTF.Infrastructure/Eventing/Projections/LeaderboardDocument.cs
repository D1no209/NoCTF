using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Eventing.Projections;

public sealed class LeaderboardDocument
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public long ProjectionVersion { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public IReadOnlyList<LeaderboardEntry> Entries { get; set; } = [];
}

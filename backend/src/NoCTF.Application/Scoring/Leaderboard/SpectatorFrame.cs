using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum SpectatorHealth { Unknown, Healthy, Down }
public enum SpectatorChangeKind { Attack, Control, Uncontrolled, DetectionFailed }

// Cached alongside the canonical scoreboard, never reconstructed from live facts when frozen.
public sealed record SpectatorService(Guid TeamId, Guid ChallengeId, SpectatorHealth Health, DateTimeOffset? CheckedAt)
{
    // Keep provenance in the cached frame so public track filtering cannot leak hidden attacks.
    public IReadOnlyList<Guid> CompromisedBy { get; init; } = [];
}
public sealed record SpectatorPoint(Guid ChallengeId, Guid? ControllerId, Guid? ObservedTeamId,
    SpectatorHealth Health, DateTimeOffset? CheckedAt, int IntervalSeconds, long PointsPerInterval);
public sealed record SpectatorChange(Guid Id, Guid Revision, DateTimeOffset At,
    SpectatorChangeKind Kind, Guid ChallengeId, Guid? TeamId, Guid? TargetTeamId, Guid? ControllerId = null);
public sealed record SpectatorFrame(string CompetitionName, CompetitionStatus Status,
    DateTimeOffset? HardeningEndsAt, IReadOnlyList<SpectatorService> Services,
    IReadOnlyList<SpectatorPoint> Points, IReadOnlyList<SpectatorChange> Changes,
    DateTimeOffset HistoryStartsAt, bool HistoryTruncated);

public static class SpectatorAudience
{
    public static SpectatorFrame Filter(SpectatorFrame frame, IReadOnlySet<Guid> teams, IReadOnlySet<Guid> challenges) => frame with
    {
        Services = frame.Services.Where(x => teams.Contains(x.TeamId) && challenges.Contains(x.ChallengeId))
            .Select(x => x with { CompromisedBy = x.CompromisedBy.Where(teams.Contains).ToArray() }).ToArray(),
        Points = frame.Points.Where(x => challenges.Contains(x.ChallengeId)).Select(x => x with
        {
            Health = x.ControllerId is Guid hidden && !teams.Contains(hidden) ? SpectatorHealth.Unknown : x.Health,
            ControllerId = x.ControllerId is Guid controller && teams.Contains(controller) ? controller : null,
            ObservedTeamId = x.ObservedTeamId is Guid observed && teams.Contains(observed) ? observed : null
        }).ToArray(),
        Changes = frame.Changes.Where(x => challenges.Contains(x.ChallengeId)
            && (x.Kind != SpectatorChangeKind.Attack || (x.TeamId is Guid attacker && teams.Contains(attacker)
                && x.TargetTeamId is Guid victim && teams.Contains(victim)))).Select(x => x with
            {
                Kind = x.ControllerId is Guid hidden && !teams.Contains(hidden) ? SpectatorChangeKind.DetectionFailed : x.Kind,
                TeamId = x.TeamId is Guid observed && teams.Contains(observed) ? observed : null,
                TargetTeamId = x.TargetTeamId is Guid target && teams.Contains(target) ? target : null,
                ControllerId = x.ControllerId is Guid controller && teams.Contains(controller) ? controller : null
            }).ToArray()
    };
}

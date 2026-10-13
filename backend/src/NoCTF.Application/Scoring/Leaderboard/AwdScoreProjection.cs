using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum AwdScoreComponent : short { Attack, DefenseReward, DefenseLoss, Availability, ExtraPenalty }

/// <summary>Safe, already grouped scoring explanations. No flag material or checker credentials.</summary>
public sealed record AwdScoreComponentProjection(
    AwdScoreComponent Component, long? Points, decimal? UnroundedPoints, decimal? RoundingDifference,
    AwdSettlementState Settlement);

public sealed record AwdRoundScoreProjection(
    Guid TeamId, Guid CompetitionChallengeId, Guid RoundId, int RoundNumber,
    IReadOnlyList<AwdScoreComponentProjection> Components,
    int AttackCount, DateTimeOffset? LastAttackAt, AwdHealth? Health, bool CompleteHealthyRound,
    bool RoundClosed = false)
{
    public long Score => Components.Aggregate(0L, (sum, component) => checked(sum + component.Points.GetValueOrDefault()));
    public long AttackPoints => Components.Where(x => x.Component == AwdScoreComponent.Attack)
        .Sum(x => x.Points.GetValueOrDefault());
    public long PositivePoints => Components.Where(x => x.Points > 0).Sum(x => x.Points!.Value);
}

using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Application.Competitions.Awd;

public enum AwdScoringRuleFailure : short { NotFound, InvalidRequest, PreviewExpired, Conflict }

public sealed record AwdScoringRuleCandidate(Guid CompetitionId, Guid CompetitionChallengeId,
    int FirstRound, int LastRound, AwdRuleChangeTiming Timing, AwdScoringPolicy Policy,
    Guid ActorUserId, string Reason, DateTimeOffset Now);

public sealed record AwdScoringProjectionOverride(Guid CompetitionChallengeId, int FirstRound,
    int LastRound, AwdScoringPolicy Policy);

public sealed record AwdScoringTeamImpact(Guid TeamId, string TeamName, long ScoreBefore, long ScoreAfter);
public sealed record AwdScoringRulePreview(string Token, DateTimeOffset ProjectedAt, DateTimeOffset ExpiresAt,
    int AffectedFlags, int AffectedRuleSnapshots, IReadOnlyList<AwdScoringTeamImpact> Teams);
public sealed record AwdScoringRuleResult(AwdScoringRuleFailure? Failure = null,
    AwdScoringRulePreview? Preview = null, Guid? EventId = null);
public sealed record AwdRoundScoringPolicyView(Guid RulesId, int Revision, DateTimeOffset EnabledAt,
    AwdScoringPolicy IssuedPolicy, AwdScoringPolicy EffectivePolicy, Guid? AppliedChangeEventId);
public sealed record AwdScoringRuleAuditView(Guid EventId, Guid? ActorUserId, string? Reason,
    int FirstRound, int LastRound, AwdRuleChangeTiming Timing, DateTimeOffset AppliedAt, AwdScoringPolicy Policy);
public sealed record AwdScoringRulesView(int RoundNumber, Guid RoundId,
    IReadOnlyList<AwdRoundScoringPolicyView> Policies, IReadOnlyList<AwdScoringRuleAuditView> RecentChanges);

public interface IAwdScoringRuleStore
{
    Task<AwdScoringRulesView?> ReadAsync(Guid competitionId, Guid challengeId, int? roundNumber, CancellationToken ct);
    Task<AwdScoringRuleResult> PreviewAsync(AwdScoringRuleCandidate candidate, CancellationToken ct);
    Task<AwdScoringRuleResult> ApplyAsync(AwdScoringRuleCandidate candidate, string token, CancellationToken ct);
}

public sealed class ManageAwdScoringRules(IAwdScoringRuleStore store)
{
    public Task<AwdScoringRuleResult> PreviewAsync(AwdScoringRuleCandidate candidate, CancellationToken ct) =>
        IsValid(candidate) ? store.PreviewAsync(candidate with { Reason = candidate.Reason.Trim() }, ct)
            : Task.FromResult(new AwdScoringRuleResult(AwdScoringRuleFailure.InvalidRequest));

    public Task<AwdScoringRuleResult> ApplyAsync(AwdScoringRuleCandidate candidate, string token, CancellationToken ct) =>
        IsValid(candidate) && !string.IsNullOrEmpty(token) && token.Length <= 2048
            ? store.ApplyAsync(candidate with { Reason = candidate.Reason.Trim() }, token, ct)
            : Task.FromResult(new AwdScoringRuleResult(AwdScoringRuleFailure.InvalidRequest));

    private static bool IsValid(AwdScoringRuleCandidate candidate) => candidate.CompetitionId != Guid.Empty
        && candidate.CompetitionChallengeId != Guid.Empty && candidate.ActorUserId != Guid.Empty
        && candidate.Policy is not null && candidate.FirstRound > 0 && candidate.LastRound >= candidate.FirstRound
        && candidate.Timing is AwdRuleChangeTiming.Immediately or AwdRuleChangeTiming.RecalculateHistory
        && !string.IsNullOrWhiteSpace(candidate.Reason) && candidate.Reason.Length <= 512;
}

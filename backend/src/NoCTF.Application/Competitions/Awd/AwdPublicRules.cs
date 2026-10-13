using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Application.Competitions.Awd;

public sealed record AwdPublicStoreRule(Guid StoreId, string Name, decimal Weight, int FlagsPerIssue);
public sealed record AwdPublicTeamHealth(bool Enabled, AwdHealth? Health, AwdCheckExecution Execution,
    AwdSettlementState Settlement, DateTimeOffset? ObservedAt, AwdHealth? LastTrustedHealth, DateTimeOffset? LastTrustedAt);
public sealed record AwdPublicRulePolicy(Guid? RulesId, int? Revision, DateTimeOffset? EffectiveAt,
    AwdAttackRewardMode AttackMode, long AttackPoints, long DefensePoints, long SlaPoints,
    int CheckerIntervalSeconds, AwdRuleOptions Options, IReadOnlyList<AwdPublicStoreRule> Stores,
    AwdScoringPolicy Scoring, Guid? ScoringChangeEventId);
public sealed record AwdPublicRulesView(DateTimeOffset ServerTime, DateTimeOffset EndsAt,
    DateTimeOffset AcceptanceEndsAt, bool Paused, bool AcceptingSubmissions, int? RoundNumber,
    Guid? RoundId, DateTimeOffset? RoundStartsAt, DateTimeOffset? RoundEndsAt,
    int RoundDurationSeconds, int HardeningDurationSeconds, AwdRuleOptions CompetitionDefaults,
    AwdRuleOverrides QuestionOverrides, bool AttackPointsOverridden, bool DefensePointsOverridden,
    bool SlaPointsOverridden, bool CheckerIntervalOverridden, bool AttackModeOverridden,
    IReadOnlyList<AwdPublicRulePolicy> IssuedPolicies, AwdPublicRulePolicy NextRound, AwdPublicTeamHealth? OwnService = null);

public interface IAwdPublicRulesReader
{
    Task<AwdPublicRulesView?> ReadAsync(Guid competitionId, Guid challengeId, DateTimeOffset now, CancellationToken ct, Guid? teamId = null);
}

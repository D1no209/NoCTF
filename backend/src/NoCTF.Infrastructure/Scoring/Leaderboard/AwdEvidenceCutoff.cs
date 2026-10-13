using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

internal static class AwdEvidenceCutoff
{
    public static bool IsVisible(DateTimeOffset updatedAt, DateTimeOffset? observedAt,
        DateTimeOffset? receivedAt, DateTimeOffset? adjudicatedAt, DateTimeOffset? attemptStartedAt,
        DateTimeOffset cutoff) => updatedAt <= cutoff && !(observedAt > cutoff
            || receivedAt > cutoff || adjudicatedAt > cutoff || attemptStartedAt > cutoff);

    public static IQueryable<GameplayFact> VisibleFacts(IQueryable<GameplayFact> facts, DateTimeOffset cutoff) =>
        facts.Where(fact => fact.UpdatedAt <= cutoff && fact.Awd != null
            && (fact.Awd.ObservedAt == null || fact.Awd.ObservedAt <= cutoff)
            && (fact.Awd.ReceivedAt == null || fact.Awd.ReceivedAt <= cutoff)
            && (fact.Awd.AdjudicatedAt == null || fact.Awd.AdjudicatedAt <= cutoff)
            && (fact.Awd.AttemptStartedAt == null || fact.Awd.AttemptStartedAt <= cutoff));

    public static AwdCheckExecution UnresolvedExecution(DateTimeOffset? attemptStartedAt, DateTimeOffset cutoff) =>
        attemptStartedAt is { } started && started <= cutoff ? AwdCheckExecution.Running : AwdCheckExecution.Pending;

    public static AwdFactDetails Read(AwdFactDetails evidence, DateTimeOffset updatedAt, DateTimeOffset cutoff) =>
        IsVisible(updatedAt, evidence.ObservedAt, evidence.ReceivedAt, evidence.AdjudicatedAt, evidence.AttemptStartedAt, cutoff)
            ? evidence
            : new AwdFactDetails
            {
                RulesId = evidence.RulesId, SampleActiveTicks = evidence.SampleActiveTicks,
                SampleDeadlineActiveTicks = evidence.SampleDeadlineActiveTicks,
                FinalScheduledCheck = evidence.FinalScheduledCheck,
                Execution = UnresolvedExecution(evidence.AttemptStartedAt, cutoff),
                Settlement = AwdSettlementState.Provisional
            };
}

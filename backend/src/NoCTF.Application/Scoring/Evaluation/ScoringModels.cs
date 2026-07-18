using NoCTF.Domain.Competitions;
using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Scoring.Evaluation;

public sealed record ScoringTeam(Guid Id, string Name, bool IsBanned, bool IsDeleted);
public sealed record ScoringChallenge(Guid Id, string Direction, bool IsDeleted, string ConfigurationJson);

public sealed record ScoringContext(
    Guid CompetitionId,
    GameMode Mode,
    string CompetitionConfigurationJson,
    IReadOnlyDictionary<Guid, ScoringTeam> Teams,
    IReadOnlyDictionary<Guid, ScoringChallenge> Challenges);

public sealed record SubmissionEventEnvelope(long Sequence, ISubmissionStreamEvent Event, DateTimeOffset Timestamp);
public sealed record DerivedScoringEvent(IScoringStreamEvent Event, Guid SourceSubmissionId);

public interface IScoringRuleEvaluator
{
    IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history);
}

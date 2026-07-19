using NoCTF.Domain.Challenges;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Processing;

public sealed record ScoringEventDecision(
    ScoringEventKind Kind,
    ScoringResult Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    string EvaluatorVersion);

public sealed record SubmissionProcessingContext(
    Submission Submission,
    IReadOnlyList<ScoringEvent> PriorEvents,
    IReadOnlyList<ChallengeFlag> ApplicableFlags,
    FixSubmissionRecord? FixRecord,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    IReadOnlyList<Submission>? PriorSubmissions = null);

public interface ISubmissionEvaluator
{
    ScoringEventDecision Evaluate(SubmissionProcessingContext context);
}

public interface ISubmissionEvaluatorCatalog
{
    ISubmissionEvaluator Get(GameMode mode);
}

public interface ISystemScoringEventEvaluator
{
    ScoringEventDecision Evaluate(ScoringEvent systemEvent);
}

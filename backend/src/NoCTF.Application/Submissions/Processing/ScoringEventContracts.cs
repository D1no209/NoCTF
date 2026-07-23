using NoCTF.Domain.Challenges;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Processing;

public sealed record ScoringEventDecision(
    ScoringEventKind Kind,
    ScoringResult Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    string EvaluatorVersion,
    Guid? VictimTeamId = null,
    SpecificationKind? SpecificationKind = null,
    Guid? SpecificationId = null);

public sealed record SubmissionProcessingContext(
    Submission Submission,
    IReadOnlyList<ScoringEvent> PriorEvents,
    IReadOnlyList<ChallengeFlag> ApplicableFlags,
    PatchUpload? PatchUpload,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    IReadOnlyList<Submission>? PriorSubmissions = null,
    DateTimeOffset? CompetitionStartTime = null);

public interface ISubmissionEvaluator
{
    ScoringEventDecision Evaluate(SubmissionProcessingContext context);
}

public interface ISubmissionEvaluatorCatalog
{
    ISubmissionEvaluator Get(GameMode mode);
}

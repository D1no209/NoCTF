using System.Text.Json;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using SubmissionEntity = NoCTF.Domain.Submissions.Submission;

namespace NoCTF.GameModes.Submission;

public sealed class GameModeSubmissionEvaluatorCatalog : ISubmissionEvaluatorCatalog
{
    private readonly IReadOnlyDictionary<GameMode, ISubmissionEvaluator> evaluators =
        new Dictionary<GameMode, ISubmissionEvaluator>
        {
            [GameMode.Ctf] = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Awd] = new AwdSubmissionEvaluator(),
            [GameMode.Awdp] = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Koh] = new KohSubmissionEvaluator()
        };

    public ISubmissionEvaluator Get(GameMode mode) => evaluators[mode];
}

internal static class ModeSubmissionEvaluatorRules
{
    public static ScoringEventDecision Reject(SubmissionEntity submission, ScoringFailureCode code) =>
        new(ScoringEventKind.SubmissionEvaluation, ScoringResult.Rejected, code,
            submission.ReceivedAt, "mode-admission-v2");
}

public sealed class CtfSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        context.Submission.Kind == SubmissionKind.Flag
            ? inner.Evaluate(context)
            : ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported);
}

public sealed class AwdSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        var submission = context.Submission;
        if (submission.Kind != SubmissionKind.Flag)
            return ModeSubmissionEvaluatorRules.Reject(submission, ScoringFailureCode.FixNotSupported);

        var configuration = AwdConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var start = context.CompetitionStartTime ?? submission.ReceivedAt;
        var currentRound = SubmissionRoundCalculator.Calculate(
            submission.ReceivedAt, start, configuration.RoundDurationSeconds);
        if (submission.ReceivedAt < start || currentRound > configuration.TotalRounds)
            return ModeSubmissionEvaluatorRules.Reject(submission, ScoringFailureCode.RoundOutOfRange);

        var candidates = context.ApplicableFlags
            .Where(flag => flag.TeamId is not null && DefaultEfSubmissionEvaluator.Matches(submission, flag))
            .OrderBy(flag => flag.Id)
            .ToList();
        if (candidates.Count == 0)
            return Decision(ScoringResult.Wrong);

        var valid = candidates.FirstOrDefault(flag =>
            (flag.ValidStart is null || flag.ValidStart <= submission.ReceivedAt)
            && (flag.ValidUntil is null || submission.ReceivedAt < flag.ValidUntil));
        if (valid is null)
            return Decision(ScoringResult.Wrong, ScoringFailureCode.FlagExpired);
        if (valid.TeamId == submission.TeamId)
            return ModeSubmissionEvaluatorRules.Reject(submission, ScoringFailureCode.SelfAttackRejected);

        var duplicate = context.PriorEvents.Any(@event =>
            @event.DeletedAt is null
            && @event.TeamId == submission.TeamId
            && @event.VictimTeamId == valid.TeamId
            && @event.CompetitionChallengeId == submission.CompetitionChallengeId
            && @event.SpecificationKind == valid.SpecificationKind
            && @event.SpecificationId == valid.SpecificationId
            && @event.Result == ScoringResult.Correct);
        return duplicate
            ? Decision(ScoringResult.Duplicate, ScoringFailureCode.DuplicateAttack, valid)
            : Decision(ScoringResult.Correct, null, valid);

        ScoringEventDecision Decision(
            ScoringResult result,
            ScoringFailureCode? failure = null,
            ChallengeFlag? matched = null) =>
            new(ScoringEventKind.SubmissionEvaluation, result, failure, submission.ReceivedAt,
                "awd-evaluator-v3", matched?.TeamId, matched?.SpecificationKind, matched?.SpecificationId);
    }
}

public sealed class AwdpSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        var configuration = Parse(context.ChallengeConfigurationJson);
        var submission = context.Submission;
        if (submission.Kind == SubmissionKind.Flag)
            return ModeSubmissionEvaluatorRules.Reject(submission, ScoringFailureCode.FlagNotSupported);
        if (submission.Kind == SubmissionKind.Fix
            && configuration.RequireBreakBeforeFix
            && !HasCorrectPrior(context, SubmissionKind.Break))
            return ModeSubmissionEvaluatorRules.Reject(submission, ScoringFailureCode.BreakRequired);
        if (HasCorrectPrior(context, submission.Kind))
            return new(ScoringEventKind.SubmissionEvaluation, ScoringResult.Duplicate,
                ScoringFailureCode.DuplicateAchievement, submission.ReceivedAt, "awdp-evaluator-v2");
        return inner.Evaluate(context);
    }

    private static bool HasCorrectPrior(SubmissionProcessingContext context, SubmissionKind kind)
    {
        var ids = context.PriorSubmissions?
            .Where(item => item.TeamId == context.Submission.TeamId
                && item.CompetitionChallengeId == context.Submission.CompetitionChallengeId
                && item.Kind == kind)
            .Select(item => item.Id)
            .ToHashSet() ?? [];
        return context.PriorEvents.Any(item =>
            item.DeletedAt is null
            && item.SubmissionId is { } submissionId
            && ids.Contains(submissionId)
            && item.Result == ScoringResult.Correct);
    }

    private static AwdpChallengeConfiguration Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AwdpChallengeConfiguration>(json, JsonOptions) ?? Default();
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    private static AwdpChallengeConfiguration Default() => new(1, null, null, true, 10, 10);
}

public sealed class KohSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        ModeSubmissionEvaluatorRules.Reject(context.Submission,
            context.Submission.Kind == SubmissionKind.Fix
                ? ScoringFailureCode.FixNotSupported
                : ScoringFailureCode.FlagNotSupported);
}

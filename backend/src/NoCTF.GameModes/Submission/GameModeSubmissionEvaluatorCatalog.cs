using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using SubmissionEntity = NoCTF.Domain.Submissions.Submission;
using System.Text.Json;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Penetration.Configuration;

namespace NoCTF.GameModes.Submission;

public sealed class GameModeSubmissionEvaluatorCatalog : ISubmissionEvaluatorCatalog
{
    private readonly IReadOnlyDictionary<GameMode, ISubmissionEvaluator> evaluators =
        new Dictionary<GameMode, ISubmissionEvaluator>
        {
            [GameMode.Ctf] = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Awd] = new AwdSubmissionEvaluator(),
            [GameMode.Awdp] = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Koh] = new KohSubmissionEvaluator(),
            [GameMode.Penetration] = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
        };

    public ISubmissionEvaluator Get(GameMode mode) => evaluators[mode];
}

internal static class ModeSubmissionEvaluatorRules
{
    public static ScoringEventDecision Reject(SubmissionEntity submission, ScoringFailureCode code) =>
        new(
            ScoringEventKind.SubmissionEvaluation,
            ScoringResult.Rejected,
            code,
            submission.ReceivedAt,
            "mode-admission-v1");
}

public sealed class CtfSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        context.Submission.Kind == SubmissionKind.Fix
            ? ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported)
            : inner.Evaluate(context);
}

public sealed class AwdSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        if (context.Submission.Kind == SubmissionKind.Fix)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported);
        if (context.Submission.SubjectTeamId is { } subject && subject == context.Submission.TeamId
            || context.Submission.VictimTeamId is { } victim && victim == context.Submission.TeamId)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.SelfAttackRejected);
        var configuration = AwdConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var start = context.CompetitionStartTime ?? context.Submission.ReceivedAt;
        var currentRound = SubmissionRoundCalculator.Calculate(
            context.Submission.ReceivedAt, start, configuration.RoundDurationSeconds);
        if (context.Submission.ReceivedAt < start || currentRound > configuration.TotalRounds)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.RoundOutOfRange);

        var duplicate = context.PriorSubmissions?.Any(previous =>
            previous.Kind == SubmissionKind.Flag
            && previous.TeamId == context.Submission.TeamId
            && previous.SubjectTeamId == context.Submission.SubjectTeamId
            && previous.VictimTeamId == context.Submission.VictimTeamId
            && previous.ServiceId == context.Submission.ServiceId
            && SubmissionRoundCalculator.Calculate(
                previous.ReceivedAt, start, configuration.RoundDurationSeconds) == currentRound
            && context.PriorEvents.Any(@event => @event.SubmissionId == previous.Id && @event.Result == ScoringResult.Correct)) == true;
        if (duplicate)
            return new ScoringEventDecision(
                ScoringEventKind.SubmissionEvaluation,
                ScoringResult.Duplicate,
                ScoringFailureCode.DuplicateAttack,
                context.Submission.ReceivedAt,
                "awd-evaluator-v2");

        var targetTeamId = context.Submission.SubjectTeamId ?? context.Submission.VictimTeamId;
        IReadOnlyList<NoCTF.Domain.Challenges.ChallengeFlag> matchingFlags = context.Submission.Flag is null
            ? []
            : context.ApplicableFlags.Where(flag =>
                flag.Flag == context.Submission.Flag
                && (flag.TeamId is null || flag.TeamId == targetTeamId)).ToList();
        if (matchingFlags.Count == 0)
            return Decision(ScoringResult.Wrong);

        if (matchingFlags.Any(IsValid))
            return Decision(ScoringResult.Correct);
        if (matchingFlags.All(IsExpired))
            return Decision(ScoringResult.Wrong, ScoringFailureCode.FlagExpired);
        return Decision(ScoringResult.Wrong);

        ScoringEventDecision Decision(ScoringResult result, ScoringFailureCode? failure = null) =>
            new(ScoringEventKind.SubmissionEvaluation, result, failure,
                context.Submission.ReceivedAt, "awd-evaluator-v2");

        bool IsValid(NoCTF.Domain.Challenges.ChallengeFlag flag) =>
            !IsFuture(flag) && !IsExpired(flag);

        bool IsFuture(NoCTF.Domain.Challenges.ChallengeFlag flag)
        {
            var flagRound = SubmissionRoundCalculator.Calculate(
                flag.ValidStart ?? start, start, configuration.RoundDurationSeconds);
            return currentRound < flagRound
                   || flag.ValidStart is { } validStart && validStart > context.Submission.ReceivedAt;
        }

        bool IsExpired(NoCTF.Domain.Challenges.ChallengeFlag flag)
        {
            var flagRound = SubmissionRoundCalculator.Calculate(
                flag.ValidStart ?? start, start, configuration.RoundDurationSeconds);
            return currentRound >= flagRound + configuration.FlagValidityRounds
                   || flag.ValidEnd is { } validEnd && validEnd < context.Submission.ReceivedAt;
        }
    }
}

public sealed class AwdpSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        var configuration = Parse(context.ChallengeConfigurationJson);
        if (context.Submission.Kind == SubmissionKind.Fix
            && configuration.RequireBreakBeforeFix
            && !HasCorrectPrior(context, SubmissionKind.Flag))
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.BreakRequired);
        if (HasCorrectPrior(context, context.Submission.Kind))
            return new(
                ScoringEventKind.SubmissionEvaluation,
                ScoringResult.Duplicate,
                ScoringFailureCode.AchievementAlreadyCompleted,
                context.Submission.ReceivedAt,
                "awdp-evaluator-v1");
        return inner.Evaluate(context);
    }

    private static bool HasCorrectPrior(SubmissionProcessingContext context, SubmissionKind kind) =>
        context.PriorSubmissions?.Any(previous =>
            previous.Kind == kind
            && previous.TeamId == context.Submission.TeamId
            && previous.ChallengeId == context.Submission.ChallengeId
            && context.PriorEvents.Any(@event => @event.SubmissionId == previous.Id && @event.Result == ScoringResult.Correct)) == true;

    private static AwdpChallengeConfiguration Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AwdpChallengeConfiguration>(json, JsonOptions)
                ?? Default();
        }
        catch (JsonException)
        {
            return Default();
        }
    }

    private static AwdpChallengeConfiguration Default() =>
        new(1, null, null, true, 10, 10);
}

public sealed class KohSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        ModeSubmissionEvaluatorRules.Reject(
            context.Submission,
            context.Submission.Kind == SubmissionKind.Fix
                ? ScoringFailureCode.FixNotSupported
                : ScoringFailureCode.FlagNotSupported);
}

public sealed class PenetrationSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        if (context.Submission.Kind == SubmissionKind.Fix)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported);
        if (context.Submission.StageId is not { } stageId)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.StageRequired);
        var configuration = Parse(context.ChallengeConfigurationJson);
        var stage = configuration.Stages.SingleOrDefault(item => item.Id == stageId);
        if (stage is null)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.StageNotFound);
        var completedStages = context.PriorSubmissions?
            .Where(previous => previous.TeamId == context.Submission.TeamId
                               && previous.ChallengeId == context.Submission.ChallengeId
                               && previous.StageId is not null
                               && context.PriorEvents.Any(@event => @event.SubmissionId == previous.Id && @event.Result == ScoringResult.Correct))
            .Select(previous => previous.StageId!.Value)
            .ToHashSet() ?? [];
        if (completedStages.Contains(stageId))
            return new(ScoringEventKind.SubmissionEvaluation, ScoringResult.Duplicate,
                ScoringFailureCode.AchievementAlreadyCompleted, context.Submission.ReceivedAt, "penetration-evaluator-v1");
        if (stage.PrerequisiteIds.Any(required => !completedStages.Contains(required)))
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.StagePrerequisiteIncomplete);
        return inner.Evaluate(context);
    }

    private static PenetrationChallengeConfiguration Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PenetrationChallengeConfiguration>(json, JsonOptions)
                ?? new(1, [], null);
        }
        catch (JsonException)
        {
            return new(1, [], null);
        }
    }
}

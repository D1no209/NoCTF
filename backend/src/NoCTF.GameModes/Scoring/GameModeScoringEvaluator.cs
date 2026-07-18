using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Ctf.Scoring;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Scoring;
using System.Security.Cryptography;
using System.Text;
using NoCTF.GameModes.Penetration.Configuration;

namespace NoCTF.GameModes.Scoring;

/// <summary>Replays permanent inputs through compile-time pure scoring rules.</summary>
public sealed class GameModeScoringEvaluator : IScoringRuleEvaluator
{
    private static readonly CtfModeScoringEvaluator Ctf = new();
    private static readonly IReadOnlyDictionary<GameMode, Func<ScoringContext, IReadOnlyList<SubmissionEventEnvelope>, IReadOnlyList<DerivedScoringEvent>>> Evaluators =
        new Dictionary<GameMode, Func<ScoringContext, IReadOnlyList<SubmissionEventEnvelope>, IReadOnlyList<DerivedScoringEvent>>>
        {
            [GameMode.Ctf] = Ctf.Evaluate,
            [GameMode.Penetration] = EvaluatePenetration,
            [GameMode.Awd] = EvaluateAwd,
            [GameMode.Awdp] = EvaluateAchievements,
            [GameMode.Koh] = EvaluateKoh
        };

    public IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history) =>
        Evaluators.TryGetValue(context.Mode, out var evaluator)
            ? evaluator(context, history)
            : throw new ArgumentOutOfRangeException(nameof(context));

    private static IReadOnlyList<DerivedScoringEvent> EvaluateAwd(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var flags = history.Select(item => item.Event).OfType<AwdFlagRotated>().ToList();
        var received = history.Select(item => item.Event).OfType<FlagSubmissionReceived>()
            .ToDictionary(item => item.SubmissionId);
        var correct = history.Select(item => item.Event).OfType<FlagSubmissionEvaluated>()
            .Where(item => item.Outcome == SubmissionOutcome.Correct);
        var rewardedAttacks = new HashSet<(Guid Attacker, Guid Victim, Guid Challenge, int Round)>();
        var penalizedVictims = new HashSet<(Guid Victim, Guid Challenge, int Round)>();
        var output = new List<DerivedScoringEvent>();

        foreach (var check in history.Select(item => item.Event).OfType<AwdServiceChecked>())
        {
            if (!IsEligible(context, check.TeamId, check.ChallengeId)) continue;
            var sourceId = StableId(check.IdempotencyKey);
            if (check.Observation == AwdServiceObservation.Up && configuration.ServiceOnlinePoints != 0)
                output.Add(new(new ScoreAwarded(context.CompetitionId, check.TeamId, check.ChallengeId,
                    configuration.ServiceOnlinePoints, "awd_service_up", check.OccurredAt, sourceId), sourceId));
            else if (check.Observation == AwdServiceObservation.Down && configuration.ServiceDownPenalty != 0)
                output.Add(new(new ScoreDeducted(context.CompetitionId, check.TeamId, check.ChallengeId,
                    configuration.ServiceDownPenalty, "awd_service_down", check.OccurredAt, sourceId), sourceId));
        }

        foreach (var result in correct)
        {
            if (!received.TryGetValue(result.SubmissionId, out var input) || !IsEligible(context, result.TeamId, result.ChallengeId))
                continue;
            var flag = flags.LastOrDefault(candidate =>
                candidate.CompetitionId == context.CompetitionId
                && candidate.ChallengeId == result.ChallengeId
                && candidate.Flag == input.Flag);
            if (flag is null || flag.TeamId == result.TeamId || !IsEligible(context, flag.TeamId, flag.ChallengeId))
                continue;

            var attackKey = (result.TeamId, flag.TeamId, flag.ChallengeId, flag.Round);
            if (rewardedAttacks.Add(attackKey))
                output.Add(new(new ScoreAwarded(context.CompetitionId, result.TeamId, result.ChallengeId,
                    configuration.AttackPoints, "awd_attack", input.ReceivedAt, result.SubmissionId), result.SubmissionId));

            var victimKey = (flag.TeamId, flag.ChallengeId, flag.Round);
            if (penalizedVictims.Add(victimKey))
                output.Add(new(new ScoreDeducted(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                    configuration.VictimPenalty, "awd_compromised", input.ReceivedAt, result.SubmissionId), result.SubmissionId));
        }
        return output;
    }

    private static IReadOnlyList<DerivedScoringEvent> EvaluateAchievements(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var competition = AwdpConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var output = new List<DerivedScoringEvent>();
        var broken = new HashSet<(Guid Team, Guid Challenge)>();
        var settled = new HashSet<(Guid Team, Guid Challenge, AchievementKind Kind, int Round)>();
        foreach (var envelope in history.OrderBy(item => item.Sequence))
        {
            if (envelope.Event is FlagSubmissionEvaluated { Outcome: SubmissionOutcome.Correct } flag)
            {
                if (!IsEligible(context, flag.TeamId, flag.ChallengeId)) continue;
                var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.Challenges[flag.ChallengeId].ConfigurationJson);
                var config = challenge.Break ?? competition.Break;
                var round = flag.OriginalRound ?? 0;
                var key = (flag.TeamId, flag.ChallengeId, AchievementKind.Break,
                    config.Settlement == AchievementSettlement.Milestone ? 0 : round);
                if (settled.Add(key))
                {
                    broken.Add((flag.TeamId, flag.ChallengeId));
                    output.Add(new(new ScoreAwarded(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                        Awdp.Scoring.AwdpScoringRules.ScoreForSuccess(config), "awdp_break", flag.EvaluatedAt, flag.SubmissionId), flag.SubmissionId));
                }
            }
            else if (envelope.Event is FixSubmissionEvaluated { Outcome: SubmissionOutcome.Correct } fix)
            {
                if (!IsEligible(context, fix.TeamId, fix.ChallengeId)) continue;
                var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.Challenges[fix.ChallengeId].ConfigurationJson);
                if (challenge.RequireBreakBeforeFix && !broken.Contains((fix.TeamId, fix.ChallengeId))) continue;
                var config = challenge.Fix ?? competition.Fix;
                var key = (fix.TeamId, fix.ChallengeId, AchievementKind.Fix,
                    config.Settlement == AchievementSettlement.Milestone ? 0 : fix.Round ?? 0);
                if (settled.Add(key))
                    output.Add(new(new ScoreAwarded(context.CompetitionId, fix.TeamId, fix.ChallengeId,
                        Awdp.Scoring.AwdpScoringRules.ScoreForSuccess(config), "awdp_fix", fix.EvaluatedAt, fix.SubmissionId), fix.SubmissionId));
            }
        }
        return output;
    }

    private static IReadOnlyList<DerivedScoringEvent> EvaluateKoh(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var output = new List<DerivedScoringEvent>();
        var observations = history.Select(item => item.Event).OfType<KohControlObserved>()
            .OrderBy(item => item.ObservedAt);
        Guid? controller = null;
        foreach (var observation in observations)
        {
            controller = observation.IsAuthoritative ? observation.ControllerTeamId : controller;
            if (controller is { } teamId && IsEligible(context, teamId, observation.ChallengeId))
            {
                var sourceId = StableId(observation.IdempotencyKey);
                output.Add(new(new ScoreAwarded(context.CompetitionId, teamId, observation.ChallengeId,
                    NoCTF.GameModes.Koh.Configuration.KohConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson).ControlPointsPerInterval,
                    "koh_control_interval", observation.ObservedAt, sourceId), sourceId));
            }
        }
        return output;
    }

    private static IReadOnlyList<DerivedScoringEvent> EvaluatePenetration(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var competition = PenetrationConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var output = new List<DerivedScoringEvent>();
        var completed = new Dictionary<(Guid Team, Guid Challenge), HashSet<Guid>>();
        var stageSolveCounts = new Dictionary<(Guid Challenge, Guid Stage), int>();
        foreach (var envelope in history.Where(item => item.Event is PenetrationStageCompleted).OrderBy(item => item.Sequence))
        {
            var input = (PenetrationStageCompleted)envelope.Event;
            if (!IsEligible(context, input.TeamId, input.ChallengeId)) continue;
            var challenge = PenetrationConfigurationUpgrader.ParseChallenge(
                context.Challenges[input.ChallengeId].ConfigurationJson);
            var stage = challenge.Stages.SingleOrDefault(item => item.Id == input.StageId);
            if (stage is null) continue;
            var teamKey = (input.TeamId, input.ChallengeId);
            if (!completed.TryGetValue(teamKey, out var teamStages))
            {
                teamStages = [];
                completed[teamKey] = teamStages;
            }
            if (teamStages.Contains(stage.Id) || stage.PrerequisiteIds.Any(prerequisite => !teamStages.Contains(prerequisite)))
                continue;

            teamStages.Add(stage.Id);
            var stageKey = (input.ChallengeId, stage.Id);
            var priorSolves = stageSolveCounts.GetValueOrDefault(stageKey);
            stageSolveCounts[stageKey] = priorSolves + 1;
            var points = CtfScoringRules.CalculatePoints(stage.Points ?? competition.DefaultPoints, priorSolves);
            var sourceId = StableId(input.IdempotencyKey);
            output.Add(new(new ScoreAwarded(context.CompetitionId, input.TeamId, input.ChallengeId,
                points, $"penetration_stage_{stage.Number}", input.OccurredAt, sourceId), sourceId));
        }
        return output;
    }

    private static bool IsEligible(ScoringContext context, Guid teamId, Guid challengeId) =>
        context.Teams.TryGetValue(teamId, out var team)
        && !team.IsBanned
        && !team.IsDeleted
        && context.Challenges.TryGetValue(challengeId, out var challenge)
        && !challenge.IsDeleted;

    private static Guid StableId(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}

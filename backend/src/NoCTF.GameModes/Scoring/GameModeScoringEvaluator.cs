using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Ctf.Scoring;

namespace NoCTF.GameModes.Scoring;

/// <summary>Replays permanent inputs through compile-time pure scoring rules.</summary>
public sealed class GameModeScoringEvaluator : IScoringRuleEvaluator
{
    public IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history) =>
        context.Mode switch
        {
            GameMode.Ctf => EvaluateCtf(context, history),
            GameMode.Penetration => EvaluateCtf(context, history),
            GameMode.Awd => EvaluateAwd(context, history),
            GameMode.Awdp => EvaluateAchievements(context, history),
            GameMode.Koh => EvaluateKoh(context, history),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };

    private static IReadOnlyList<DerivedScoringEvent> EvaluateCtf(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var configuration = context.Mode == GameMode.Ctf
            ? CtfConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson)
            : new CtfConfiguration(1, new(500, 100, 450), []);
        var evaluated = history.Select(envelope => envelope.Event)
            .OfType<FlagSubmissionEvaluated>()
            .Where(result => result.Outcome == SubmissionOutcome.Correct)
            .OrderBy(result => history.First(item => ReferenceEquals(item.Event, result)).Sequence)
            .ToList();
        var received = history.Select(envelope => envelope.Event)
            .OfType<FlagSubmissionReceived>()
            .ToDictionary(item => item.SubmissionId);
        var results = new List<DerivedScoringEvent>();

        foreach (var challengeGroup in evaluated.GroupBy(result => result.ChallengeId))
        {
            var priorSolves = 0;
            foreach (var result in challengeGroup)
            {
                if (!IsEligible(context, result.TeamId, result.ChallengeId) || !received.TryGetValue(result.SubmissionId, out var input))
                    continue;
                var points = CtfScoringRules.CalculatePoints(configuration.DefaultPoints, priorSolves);
                results.Add(new(new ScoreAwarded(
                    context.CompetitionId,
                    result.TeamId,
                    result.ChallengeId,
                    points,
                    "solve",
                    input.ReceivedAt,
                    result.SubmissionId), result.SubmissionId));
                results.Add(new(new SolveRecorded(
                    context.CompetitionId,
                    result.TeamId,
                    result.ChallengeId,
                    input.ReceivedAt,
                    result.SubmissionId), result.SubmissionId));

                if (priorSolves < configuration.BloodRewards.Count)
                {
                    var blood = CtfScoringRules.CalculateBlood(
                        configuration.BloodRewards[priorSolves],
                        configuration.DefaultPoints,
                        points);
                    results.Add(new(new ScoreAwarded(
                        context.CompetitionId,
                        result.TeamId,
                        result.ChallengeId,
                        blood,
                        $"blood_{priorSolves + 1}",
                        input.ReceivedAt,
                        result.SubmissionId), result.SubmissionId));
                }
                priorSolves++;
            }
        }
        return results;
    }

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
        var output = new List<DerivedScoringEvent>();
        var achieved = new HashSet<(Guid Team, Guid Challenge, string Kind)>();
        foreach (var result in history.Select(item => item.Event).OfType<FixSubmissionEvaluated>()
                     .Where(item => item.Outcome == SubmissionOutcome.Correct))
        {
            if (!IsEligible(context, result.TeamId, result.ChallengeId) || !achieved.Add((result.TeamId, result.ChallengeId, "fix")))
                continue;
            output.Add(new(new ScoreAwarded(context.CompetitionId, result.TeamId, result.ChallengeId,
                100, "awdp_fix", result.EvaluatedAt, result.SubmissionId), result.SubmissionId));
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
                var sourceId = Guid.CreateVersion7(observation.ObservedAt);
                output.Add(new(new ScoreAwarded(context.CompetitionId, teamId, observation.ChallengeId,
                    10, "koh_control_interval", observation.ObservedAt, sourceId), sourceId));
            }
        }
        return output;
    }

    private static bool IsEligible(ScoringContext context, Guid teamId, Guid challengeId) =>
        context.Teams.TryGetValue(teamId, out var team)
        && !team.IsBanned
        && !team.IsDeleted
        && context.Challenges.TryGetValue(challengeId, out var challenge)
        && !challenge.IsDeleted;
}

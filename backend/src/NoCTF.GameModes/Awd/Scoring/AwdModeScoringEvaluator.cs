using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Awd.Scoring;

public sealed class AwdModeScoringEvaluator : IGameModeScoringEvaluator
{
    public GameMode Mode => GameMode.Awd;

    public IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var flags = history.Select(item => item.Event).OfType<AwdFlagRotated>().ToList();
        var received = history.Select(item => item.Event).OfType<FlagSubmissionReceived>()
            .ToDictionary(item => item.SubmissionId);
        var correct = history.Select(item => item.Event).OfType<FlagSubmissionEvaluated>()
            .Where(item => item.Outcome == SubmissionOutcome.Correct);
        var rewarded = new HashSet<(Guid Attacker, Guid Challenge, int Round)>();
        var penalized = new HashSet<(Guid Victim, Guid Challenge, int Round)>();
        var output = new List<DerivedScoringEvent>();

        foreach (var check in history.Select(item => item.Event).OfType<AwdServiceChecked>())
        {
            if (!GameModeScoringSupport.IsEligible(context, check.TeamId, check.ChallengeId)) continue;
            var sourceId = GameModeScoringSupport.StableId(check.IdempotencyKey);
            if (check.Observation == AwdServiceObservation.Up && configuration.ServiceOnlinePoints != 0)
                output.Add(new(new ScoreAwarded(context.CompetitionId, check.TeamId, check.ChallengeId,
                    configuration.ServiceOnlinePoints, ScoringReason.AwdServiceUp, check.OccurredAt, sourceId), sourceId));
            else if (check.Observation == AwdServiceObservation.Down && configuration.ServiceDownPenalty != 0)
                output.Add(new(new ScoreDeducted(context.CompetitionId, check.TeamId, check.ChallengeId,
                    configuration.ServiceDownPenalty, ScoringReason.AwdServiceDown, check.OccurredAt, sourceId), sourceId));
        }

        foreach (var result in correct)
        {
            if (!received.TryGetValue(result.SubmissionId, out var input)
                || !GameModeScoringSupport.IsEligible(context, result.TeamId, result.ChallengeId)) continue;
            var flag = flags.Where(candidate => candidate.ChallengeId == result.ChallengeId
                    && candidate.Flag == input.Flag
                    && candidate.OccurredAt <= input.ReceivedAt)
                .OrderByDescending(candidate => candidate.OccurredAt)
                .FirstOrDefault();
            if (flag is null || flag.TeamId == result.TeamId
                || !GameModeScoringSupport.IsEligible(context, flag.TeamId, flag.ChallengeId)) continue;

            var attackKey = (result.TeamId, flag.ChallengeId, flag.Round);
            if (rewarded.Add(attackKey))
            {
                output.Add(new(new ScoreAwarded(context.CompetitionId, result.TeamId, result.ChallengeId,
                    configuration.AttackPoints, ScoringReason.AwdAttack, input.ReceivedAt, result.SubmissionId), result.SubmissionId));
                output.Add(new(new AwdAttackRewarded(context.CompetitionId, result.TeamId, flag.TeamId,
                    result.ChallengeId, flag.Round, input.ReceivedAt, result.SubmissionId), result.SubmissionId));
            }

            var victimKey = (flag.TeamId, flag.ChallengeId, flag.Round);
            if (penalized.Add(victimKey))
            {
                output.Add(new(new ScoreDeducted(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                    configuration.VictimPenalty, ScoringReason.AwdCompromised, input.ReceivedAt, result.SubmissionId), result.SubmissionId));
                output.Add(new(new AwdVictimPenaltyApplied(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                    flag.Round, input.ReceivedAt, result.SubmissionId), result.SubmissionId));
            }
        }
        return output;
    }

}

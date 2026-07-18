using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Ctf.Scoring;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Scoring;

internal sealed class CtfModeScoringEvaluator : IGameModeScoringEvaluator
{
    public GameMode Mode => GameMode.Ctf;

    public IReadOnlyList<DerivedScoringEvent> Evaluate(ScoringContext context, IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var competition = CtfConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var received = history.Select(item => item.Event).OfType<FlagSubmissionReceived>()
            .ToDictionary(item => item.SubmissionId);
        var results = history.Select(item => (item.Sequence, Event: item.Event))
            .Where(item => item.Event is FlagSubmissionEvaluated { Outcome: SubmissionOutcome.Correct })
            .Select(item => (item.Sequence, Result: (FlagSubmissionEvaluated)item.Event))
            .OrderBy(item => item.Sequence)
            .ToList();
        var output = new List<DerivedScoringEvent>();

        foreach (var group in results.GroupBy(item => item.Result.ChallengeId))
        {
            var challenge = context.Challenges[group.Key];
            var challengeConfig = CtfConfigurationUpgrader.ParseChallenge(challenge.ConfigurationJson);
            var points = challengeConfig.Points ?? competition.DefaultPoints;
            var bloodRewards = challengeConfig.BloodRewards ?? competition.BloodRewards;
            var priorSolves = 0;
            foreach (var item in group)
            {
                if (!IsEligible(context, item.Result.TeamId, item.Result.ChallengeId)
                    || !received.TryGetValue(item.Result.SubmissionId, out var input))
                    continue;
                var solvePoints = CtfScoringRules.CalculatePoints(points, priorSolves);
                output.Add(new(new ScoreAwarded(context.CompetitionId, item.Result.TeamId, item.Result.ChallengeId,
                    solvePoints, ScoringReason.Solve, input.ReceivedAt, item.Result.SubmissionId), item.Result.SubmissionId));
                output.Add(new(new CtfSolveRecorded(context.CompetitionId, item.Result.TeamId, item.Result.ChallengeId,
                    input.ReceivedAt, item.Result.SubmissionId), item.Result.SubmissionId));
                if (priorSolves < bloodRewards.Count)
                    output.Add(new(new ScoreAwarded(context.CompetitionId, item.Result.TeamId, item.Result.ChallengeId,
                        CtfScoringRules.CalculateBlood(bloodRewards[priorSolves], points, solvePoints),
                        ScoringReason.Blood, input.ReceivedAt, item.Result.SubmissionId), item.Result.SubmissionId));
                priorSolves++;
            }
        }
        return output;
    }

    private static bool IsEligible(ScoringContext context, Guid teamId, Guid challengeId) =>
        context.Teams.TryGetValue(teamId, out var team) && !team.IsBanned && !team.IsDeleted
        && context.Challenges.TryGetValue(challengeId, out var challenge) && !challenge.IsDeleted;
}

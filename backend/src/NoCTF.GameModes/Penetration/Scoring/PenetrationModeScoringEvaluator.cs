using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Ctf.Scoring;
using NoCTF.GameModes.Penetration.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Penetration.Scoring;

public sealed class PenetrationModeScoringEvaluator : IGameModeScoringEvaluator
{
    public GameMode Mode => GameMode.Penetration;

    public IReadOnlyList<DerivedScoringEvent> Evaluate(ScoringContext context, IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var competition = PenetrationConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        var output = new List<DerivedScoringEvent>();
        var completed = new Dictionary<(Guid Team, Guid Challenge), HashSet<Guid>>();
        var stageSolveCounts = new Dictionary<(Guid Challenge, Guid Stage), int>();
        foreach (var envelope in history.Where(item => item.Event is PenetrationStageCompleted).OrderBy(item => item.Sequence))
        {
            var input = (PenetrationStageCompleted)envelope.Event;
            if (!GameModeScoringSupport.IsEligible(context, input.TeamId, input.ChallengeId)) continue;
            var challenge = PenetrationConfigurationUpgrader.ParseChallenge(context.Challenges[input.ChallengeId].ConfigurationJson);
            var stage = challenge.Stages.SingleOrDefault(item => item.Id == input.StageId);
            if (stage is null) continue;
            var teamKey = (input.TeamId, input.ChallengeId);
            if (!completed.TryGetValue(teamKey, out var teamStages))
                completed[teamKey] = teamStages = [];
            if (teamStages.Contains(stage.Id) || stage.PrerequisiteIds.Any(item => !teamStages.Contains(item))) continue;
            teamStages.Add(stage.Id);
            var stageKey = (input.ChallengeId, stage.Id);
            var priorSolves = stageSolveCounts.GetValueOrDefault(stageKey);
            stageSolveCounts[stageKey] = priorSolves + 1;
            var points = CtfScoringRules.CalculatePoints(stage.Points ?? competition.DefaultPoints, priorSolves);
            var sourceId = GameModeScoringSupport.StableId(input.IdempotencyKey);
            output.Add(new(new ScoreAwarded(context.CompetitionId, input.TeamId, input.ChallengeId,
                points, ScoringReason.PenetrationStage, input.OccurredAt, sourceId), sourceId));
            output.Add(new(new PenetrationStageScored(context.CompetitionId, input.TeamId, input.ChallengeId,
                input.StageId, input.OccurredAt, sourceId), sourceId));
        }
        return output;
    }

}

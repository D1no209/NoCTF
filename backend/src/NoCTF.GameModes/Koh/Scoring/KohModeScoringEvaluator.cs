using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Koh.Scoring;

public sealed class KohModeScoringEvaluator : IGameModeScoringEvaluator
{
    public GameMode Mode => GameMode.Koh;

    public IReadOnlyList<DerivedScoringEvent> Evaluate(ScoringContext context, IReadOnlyList<SubmissionEventEnvelope> history)
    {
        var points = KohConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson).ControlPointsPerInterval;
        var output = new List<DerivedScoringEvent>();
        Guid? controller = null;
        foreach (var observation in history.Select(item => item.Event).OfType<KohControlObserved>().OrderBy(item => item.ObservedAt))
        {
            controller = observation.IsAuthoritative ? observation.ControllerTeamId : controller;
            if (controller is not { } teamId || !GameModeScoringSupport.IsEligible(context, teamId, observation.ChallengeId)) continue;
            var sourceId = GameModeScoringSupport.StableId(observation.IdempotencyKey);
            output.Add(new(new ScoreAwarded(context.CompetitionId, teamId, observation.ChallengeId,
                points, ScoringReason.KohControlInterval, observation.ObservedAt, sourceId), sourceId));
            output.Add(new(new KohControlIntervalScored(context.CompetitionId, teamId, observation.ChallengeId,
                observation.ObservedAt, sourceId), sourceId));
        }
        return output;
    }

}

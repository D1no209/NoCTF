using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Awdp.Scoring;

public sealed class AwdpModeScoringEvaluator : IGameModeScoringEvaluator
{
    public GameMode Mode => GameMode.Awdp;

    public IReadOnlyList<DerivedScoringEvent> Evaluate(
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
                if (!GameModeScoringSupport.IsEligible(context, flag.TeamId, flag.ChallengeId)) continue;
                var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.Challenges[flag.ChallengeId].ConfigurationJson);
                var config = challenge.Break ?? competition.Break;
                var round = flag.OriginalRound ?? 0;
                var key = (flag.TeamId, flag.ChallengeId, AchievementKind.Break,
                    config.Settlement == AchievementSettlement.Milestone ? 0 : round);
                if (!settled.Add(key)) continue;
                broken.Add((flag.TeamId, flag.ChallengeId));
                output.Add(new(new ScoreAwarded(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                    AwdpScoringRules.ScoreForSuccess(config), ScoringReason.AwdpBreak,
                    flag.EvaluatedAt, flag.SubmissionId), flag.SubmissionId));
                output.Add(new(new AwdpBreakAchieved(context.CompetitionId, flag.TeamId, flag.ChallengeId,
                    round, flag.EvaluatedAt, flag.SubmissionId), flag.SubmissionId));
            }
            else if (envelope.Event is FixSubmissionEvaluated { Outcome: SubmissionOutcome.Correct } fix)
            {
                if (!GameModeScoringSupport.IsEligible(context, fix.TeamId, fix.ChallengeId)) continue;
                var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.Challenges[fix.ChallengeId].ConfigurationJson);
                if (challenge.RequireBreakBeforeFix && !broken.Contains((fix.TeamId, fix.ChallengeId))) continue;
                var config = challenge.Fix ?? competition.Fix;
                var round = fix.Round ?? 0;
                var key = (fix.TeamId, fix.ChallengeId, AchievementKind.Fix,
                    config.Settlement == AchievementSettlement.Milestone ? 0 : round);
                if (!settled.Add(key)) continue;
                output.Add(new(new ScoreAwarded(context.CompetitionId, fix.TeamId, fix.ChallengeId,
                    AwdpScoringRules.ScoreForSuccess(config), ScoringReason.AwdpFix,
                    fix.EvaluatedAt, fix.SubmissionId), fix.SubmissionId));
                output.Add(new(new AwdpFixAchieved(context.CompetitionId, fix.TeamId, fix.ChallengeId,
                    round, fix.EvaluatedAt, fix.SubmissionId), fix.SubmissionId));
            }
        }
        return output;
    }

}

using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Activities;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Leaderboard;

/// <summary>Calculates one spendable score using the same mode, discount and theory rules as the scoreboard.</summary>
public sealed class TeamScoreCalculator(ILeaderboardProjectorCatalog projectors)
{
    public long Calculate(LeaderboardProjectionInput input, Guid teamId)
    {
        input = ChallengeTimingProjection.ForScoring(ChallengeTimingProjection.WithCurrentResults(input));
        var raw = input.Mode switch
        {
            GameMode.Ctf => CtfLeaderboardProjection.Project(input, teamId),
            GameMode.Awdp => AwdpDynamicLeaderboardProjection.Project(input, teamId),
            GameMode.Koh => new KohLeaderboardProjector().Project(input, teamId),
            GameMode.Awd => projectors.Get(input.Mode).Project(input with { Teams = input.Teams.Where(team => team.Id == teamId).ToArray() }),
            _ => projectors.Get(input.Mode).Project(input)
        };
        var entry = raw.Entries.SingleOrDefault(item => item.TeamId == teamId);
        if (entry is null) return 0;
        var score = entry.Score;
        var unlocks = (input.WriteUpUnlocks ?? []).Where(item => item.TeamId == teamId
            && item.UnlockedAt <= input.ProjectedAt).ToArray();
        if (unlocks.Length != 0)
        {
            var gross = WriteUpBenefitProjection.Gross(input, raw);
            foreach (var unlock in unlocks)
                score = checked(score - ChallengeWriteUpPolicy.Deduction(
                    gross.GetValueOrDefault((teamId, unlock.CompetitionChallengeId)), unlock.DeductionPercent));
        }
        if (input.Mode == GameMode.Awd && input.AwdMinimumTotalScore is { } floor)
            score = Math.Max(score, floor);
        if (TheoryActivityPolicy.Supports(input.Mode))
            score = checked(score + (input.TheoryScores?.SingleOrDefault(item => item.TeamId == teamId)?.Points ?? 0));
        return score;
    }
}

using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Leaderboard;

/// <summary>Discounts only positive benefits after the authoritative mode rules have projected them.</summary>
internal static class WriteUpBenefitProjection
{
    public static bool CanEarnBlood(LeaderboardProjectionInput input, LeaderboardGameplayFact solve) =>
        !(input.WriteUpUnlocks ?? []).Any(x => x.TeamId == solve.TeamId
            && x.CompetitionChallengeId == solve.CompetitionChallengeId && x.UnlockedAt <= solve.OccurredAt);

    public static ScoreboardProjection Apply(LeaderboardProjectionInput input, GameModeLeaderboardProjection raw,
        ScoreboardProjection scoreboard, ILeaderboardProjectorCatalog catalog)
    {
        var unlocks = (input.WriteUpUnlocks ?? []).Where(x => x.UnlockedAt <= input.ProjectedAt)
            .ToDictionary(x => (x.TeamId, x.CompetitionChallengeId));
        var gross = Gross(input, raw, scoreboard);
        var columns = scoreboard.Schema.Columns.ToDictionary(x => x.Index);
        var rounds = scoreboard.Schema.Rounds.ToDictionary(x => x.Id);
        var beforeWindow = new Dictionary<(Guid TeamId, Guid ChallengeId), long>();
        if (unlocks.Count != 0 && input.Mode == GameMode.Awd && input.AwdAggregates is not null)
            foreach (var row in input.AwdAggregates) beforeWindow[(row.TeamId, row.CompetitionChallengeId)] = row.PositivePointsBeforeWindow;
        if (unlocks.Count != 0 && input.Mode == GameMode.Awdp && rounds.Count != 0)
        {
            var start = rounds.Values.Min(x => x.StartAt);
            var earlier = input with { ProjectedAt = start, CompetitionStatus = CompetitionStatus.Running, ScoreboardGameplayFacts = null };
            var earlierRaw = catalog.Get(input.Mode).Project(earlier);
            beforeWindow = Gross(earlier, earlierRaw, scoreboard);
        }
        var allocations = scoreboard.EntryAllocations.ToList();
        var adjusted = new List<ScoreboardTeam>();
        foreach (var team in scoreboard.Snapshot.Teams)
        {
            var benefits = gross.Where(x => x.Key.TeamId == team.TeamId && (x.Value != 0 || unlocks.ContainsKey(x.Key))).OrderBy(x => x.Key.ChallengeId).Select(x =>
            {
                unlocks.TryGetValue(x.Key, out var unlock);
                return new ScoreboardChallengeBenefit(x.Key.ChallengeId, x.Value,
                    unlock is null ? 0 : ChallengeWriteUpPolicy.Deduction(x.Value, unlock.DeductionPercent),
                    unlock?.DeductionPercent, unlock?.UnlockedAt);
            }).ToArray();
            // Retain an observable zero-benefit receipt for unsolved or unscored challenges.
            benefits = benefits.Concat(unlocks.Where(x => x.Key.TeamId == team.TeamId && !gross.ContainsKey(x.Key))
                .Select(x => new ScoreboardChallengeBenefit(x.Key.CompetitionChallengeId, 0, 0,
                    x.Value.DeductionPercent, x.Value.UnlockedAt))).OrderBy(x => x.CompetitionChallengeId).ToArray();
            var slots = team.Slots.ToDictionary(x => x.ColumnIndex);
            if (!raw.Entries.Any(x => x.TeamId == team.TeamId)) { adjusted.Add(team with { ChallengeBenefits = benefits }); continue; }
            var visibleDeduction = 0L;
            foreach (var group in team.Slots.GroupBy(x => columns[x.ColumnIndex].CompetitionChallengeId))
            {
                if (!unlocks.TryGetValue((team.TeamId, group.Key), out var unlock)) continue;
                var prefix = beforeWindow.GetValueOrDefault((team.TeamId, group.Key));
                foreach (var slot in group.OrderBy(x => columns[x.ColumnIndex].RoundId is Guid id
                             ? rounds[id].StartAt : DateTimeOffset.MinValue).ThenBy(x => x.ColumnIndex))
                {
                    var manual = slot.Breakdowns.Where(x => x.Kind == ScoreboardBreakdownKind.ManualAdjustment).Sum(x => x.EarnedPoints);
                    var positive = Math.Max(0, checked(slot.EarnedPoints.GetValueOrDefault() - manual));
                    var deduction = checked(ChallengeWriteUpPolicy.Deduction(checked(prefix + positive), unlock.DeductionPercent)
                        - ChallengeWriteUpPolicy.Deduction(prefix, unlock.DeductionPercent));
                    prefix = checked(prefix + positive);
                    visibleDeduction = checked(visibleDeduction + deduction);
                    var entry = new ScoreboardSlotEntry(unlock.GameplayFactId, ScoreboardEntryKind.WriteUp,
                        ScoreboardEntryOutcome.Succeeded, null, null, unlock.UnlockedAt, null, 0, deduction, -deduction);
                    slots[slot.ColumnIndex] = slot with { DeductedPoints = slot.DeductedPoints is null ? null : checked(slot.DeductedPoints + deduction),
                        NetPoints = slot.NetPoints is null ? null : checked(slot.NetPoints - deduction), EntryCount = checked(slot.EntryCount + 1),
                        Breakdowns = slot.Breakdowns.Append(new ScoreboardBreakdown(ScoreboardBreakdownKind.WriteUp, 1, 1, 0, deduction, -deduction)).ToArray(),
                        Entries = slot.Entries.Append(entry).OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(5).ToArray() };
                    allocations.Add(new(team.TeamId, slot.ColumnIndex, entry));
                }
            }
            var totalDeduction = benefits.Aggregate(0L, (total, x) => checked(total + x.WriteUpDeductionPoints));
            adjusted.Add(team with { TotalScore = checked(team.TotalScore - totalDeduction),
                ScoreOutsideWindow = checked(team.ScoreOutsideWindow - totalDeduction + visibleDeduction),
                Slots = team.Slots.Select(x => slots[x.ColumnIndex]).ToArray(), ChallengeBenefits = benefits });
        }
        var ranked = adjusted.Where(x => x.RankingState == ScoreboardRankingState.Eligible && x.Rank is not null)
            .GroupBy(x => x.TrackKey, StringComparer.OrdinalIgnoreCase).SelectMany(track => Rank(input, raw, track)
                .Select((team, index) => team with { Rank = index + 1 })).ToDictionary(x => x.TeamId);
        var teams = adjusted.Select(x => ranked.GetValueOrDefault(x.TeamId, x)).ToArray();
        var version = scoreboard.Snapshot.Version;
        if (unlocks.Count != 0)
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(version + ":" + string.Join(';', teams.OrderBy(x => x.TeamId)
                .Select(x => $"{x.TeamId:N}:{x.TotalScore}:{x.Rank}:" + string.Join(',', x.ChallengeBenefits.Select(b =>
                    $"{b.CompetitionChallengeId:N}:{b.WriteUpDeductionPercent}:{b.WriteUpDeductionPoints}:{b.WriteUpUnlockedAt?.UtcTicks}"))))));
            version = Math.Max(1, BitConverter.ToInt64(digest, 0) & long.MaxValue);
        }
        return scoreboard with { Snapshot = scoreboard.Snapshot with { Teams = teams, Version = version }, EntryAllocations = allocations };
    }

    private static Dictionary<(Guid TeamId, Guid ChallengeId), long> Gross(LeaderboardProjectionInput input,
        GameModeLeaderboardProjection raw, ScoreboardProjection scoreboard)
    {
        if (input.Mode == GameMode.Awd && input.AwdAggregates is not null)
            return input.AwdAggregates.ToDictionary(x => (x.TeamId, x.CompetitionChallengeId), x => x.PositivePoints);
        var manual = input.GameplayFacts.Where(x => x.Kind == GameplayFactKind.ManualAdjustment
            && x.Result == GameplayFactResult.Applied && x.TeamId is not null && x.CompetitionChallengeId is not null)
            .GroupBy(x => (x.TeamId!.Value, x.CompetitionChallengeId!.Value)).ToDictionary(x => x.Key,
                x => x.Sum(f => checked(ProjectionPenalties.ParseDelta(f.Value) * f.Multiplicity)));
        if (input.Mode == GameMode.Awd)
        {
            var columns = scoreboard.Schema.Columns.ToDictionary(x => x.Index);
            return scoreboard.Snapshot.Teams.SelectMany(team => team.Slots.Select(slot => new { team.TeamId,
                ChallengeId = columns[slot.ColumnIndex].CompetitionChallengeId, Points = slot.EarnedPoints.GetValueOrDefault()
                    - slot.Breakdowns.Where(x => x.Kind == ScoreboardBreakdownKind.ManualAdjustment).Sum(x => x.EarnedPoints) }))
                .GroupBy(x => (x.TeamId, x.ChallengeId)).ToDictionary(x => x.Key, x => Math.Max(0, x.Sum(v => v.Points)));
        }
        return raw.Cells.GroupBy(x => (x.TeamId, ChallengeId: x.CompetitionChallengeId)).ToDictionary(x => x.Key, x =>
            Math.Max(0, input.Mode switch
            {
                GameMode.Ctf => x.Sum(c => checked(c.BasePoints.GetValueOrDefault() + c.BloodAwardPoints.GetValueOrDefault())),
                GameMode.Awdp => x.Sum(c => checked(c.AttackScore.GetValueOrDefault() + c.DefenseScore.GetValueOrDefault())),
                GameMode.Koh => checked(x.Sum(c => c.Score) - manual.GetValueOrDefault(x.Key)),
                _ => 0
            }));
    }

    private static IOrderedEnumerable<ScoreboardTeam> Rank(LeaderboardProjectionInput input,
        GameModeLeaderboardProjection raw, IEnumerable<ScoreboardTeam> teams)
    {
        var entries = raw.Entries.ToDictionary(x => x.TeamId);
        var facts = input.Teams.ToDictionary(x => x.Id);
        var ordered = teams.OrderByDescending(x => x.TotalScore);
        if (input.Mode == GameMode.Ctf)
            return ordered.ThenBy(x => entries.GetValueOrDefault(x.TeamId)?.LastScoreAt ?? DateTimeOffset.MaxValue)
                .ThenByDescending(x => entries.GetValueOrDefault(x.TeamId)?.SolveCount ?? 0)
                .ThenBy(x => facts[x.TeamId].RegisteredAt).ThenBy(x => x.TeamId);
        if (input.Mode == GameMode.Awd)
        {
            long AttackPoints(ScoreboardTeam team)
            {
                var original = entries[team.TeamId].AttackScore.GetValueOrDefault();
                return input.AwdAggregates is null ? original : checked(original - input.AwdAggregates.Where(x => x.TeamId == team.TeamId)
                    .Sum(row => ChallengeWriteUpPolicy.Deduction(row.AttackPoints,
                        (input.WriteUpUnlocks ?? []).SingleOrDefault(x => x.TeamId == row.TeamId
                            && x.CompetitionChallengeId == row.CompetitionChallengeId)?.DeductionPercent ?? 0)));
            }
            return ordered.ThenByDescending(AttackPoints).ThenByDescending(x => entries[x.TeamId].UpRoundCount)
                .ThenByDescending(x => entries[x.TeamId].AttackCount).ThenBy(x => entries[x.TeamId].LastScoreAt ?? DateTimeOffset.MaxValue)
                .ThenBy(x => facts[x.TeamId].RegisteredAt).ThenBy(x => x.TeamId);
        }
        if (input.Mode == GameMode.Awdp)
            return ordered.ThenByDescending(x => entries[x.TeamId].FixCount).ThenByDescending(x => entries[x.TeamId].AttackCount)
                .ThenBy(x => entries[x.TeamId].PenaltyScore.GetValueOrDefault()).ThenBy(x => entries[x.TeamId].LastFixAt ?? DateTimeOffset.MaxValue)
                .ThenBy(x => facts[x.TeamId].RegisteredAt).ThenBy(x => x.TeamId);
        return ordered.ThenByDescending(x => entries[x.TeamId].SolveCount).ThenByDescending(x => entries[x.TeamId].ControlledChallengeCount)
            .ThenBy(x => entries[x.TeamId].FirstControlAt ?? DateTimeOffset.MaxValue).ThenBy(x => facts[x.TeamId].RegisteredAt).ThenBy(x => x.TeamId);
    }
}

using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Brackets;

public enum LiveSoloTournamentStandingState { Active, Qualified, Eliminated }
public sealed record LiveSoloTournamentStanding(Guid TeamId, int Seed, int Wins, int Losses, int Buchholz,
    int Byes, LiveSoloTournamentStandingState State);

public static class LiveSoloTournamentPolicy
{
    public static IReadOnlyList<LiveSoloTournamentStage> Stages(LiveSoloCompetitionModeConfiguration configuration) =>
        configuration.TournamentStages.Count != 0 ? configuration.TournamentStages.OrderBy(x => x.Position).ToArray()
            : [new() { Format = configuration.BracketFormat, RequiredWins = configuration.RequiredWins,
                AdvanceCount = configuration.BracketFormat == LiveSoloBracketFormat.Swiss ? 0 : 1 }];

    public static IReadOnlyList<LiveSoloMatch> Generate(Guid competitionId, IReadOnlyList<Guid> seeds,
        LiveSoloCompetitionModeConfiguration configuration, int stageNumber, DateTimeOffset now)
    {
        var stage = Stages(configuration)[stageNumber - 1];
        IReadOnlyList<LiveSoloMatch> matches;
        if (seeds.Count == 1)
        {
            var match = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = competitionId, TournamentStage = stageNumber,
                Lane = LiveSoloBracketLane.Winners, Stage = 1, RequiredWins = stage.RequiredWins, State = LiveSoloMatchState.Completed,
                WinnerTeamId = seeds[0], CreatedAt = now, CompletedAt = now };
            match.Slots = [new() { MatchId = match.Id, Side = LiveSoloSide.Left, Source = LiveSoloSlotSource.Seed, Seed = 1, TeamId = seeds[0], Resolved = true },
                new() { MatchId = match.Id, Side = LiveSoloSide.Right, Source = LiveSoloSlotSource.Bye, Resolved = true }];
            NoCTF.Application.LiveSolo.Rounds.LiveSoloResults.Confirm(match, LiveSoloEndReason.Bye, now);
            matches = [match];
        }
        else if (stage.Format == LiveSoloBracketFormat.Swiss)
            matches = SwissRound(competitionId, seeds, [], stage, configuration.StageRules, stageNumber, now);
        else
            matches = LiveSoloBracketPolicy.Generate(competitionId, seeds, new() { BracketFormat = stage.Format,
                RequiredWins = stage.RequiredWins, StageRules = configuration.StageRules.Where(x => x.TournamentStage == stageNumber).ToList() }, now);
        foreach (var match in matches) match.TournamentStage = stageNumber;
        return matches;
    }

    public static IReadOnlyList<Guid> Seeds(IReadOnlyList<LiveSoloMatch> matches) => matches.SelectMany(x => x.Slots)
        .Where(x => x.Seed != null && x.TeamId != null).GroupBy(x => x.TeamId!.Value)
        .OrderBy(x => x.Min(s => s.Seed)).Select(x => x.Key).ToArray();

    public static bool Complete(IReadOnlyList<LiveSoloMatch> matches, LiveSoloTournamentStage stage)
    {
        if (matches.Count == 0 || matches.Any(x => x.PendingCorrectionId != null
            || x.State is not (LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled))) return false;
        if (stage.Format == LiveSoloBracketFormat.Swiss)
            return Standings(matches, stage).All(x => x.State != LiveSoloTournamentStandingState.Active);
        return matches.All(x => x.State == LiveSoloMatchState.Completed || x.Conditional)
            && Champion(matches, stage.Format) is not null;
    }

    public static Guid? Champion(IReadOnlyList<LiveSoloMatch> matches, LiveSoloBracketFormat format)
    {
        if (format == LiveSoloBracketFormat.Swiss) return null;
        var final = matches.Where(x => x.Lane == LiveSoloBracketLane.Winners).MaxBy(x => x.Stage);
        if (matches.Count == 1 && matches[0].Slots.Count(x => x.TeamId != null) == 1) final = matches[0];
        else if (format == LiveSoloBracketFormat.DoubleElimination)
        {
            var reset = matches.SingleOrDefault(x => x.Lane == LiveSoloBracketLane.ResetFinal);
            final = reset?.State == LiveSoloMatchState.Completed ? reset : reset?.State == LiveSoloMatchState.Canceled
                ? matches.SingleOrDefault(x => x.Lane == LiveSoloBracketLane.GrandFinal) : null;
        }
        return final?.State == LiveSoloMatchState.Completed && final.PendingCorrectionId is null ? final.WinnerTeamId : null;
    }

    public static IReadOnlyList<LiveSoloTournamentStanding> Standings(IReadOnlyList<LiveSoloMatch> matches, LiveSoloTournamentStage stage)
    {
        var seeds = Seeds(matches);
        var completed = matches.Where(x => x.State == LiveSoloMatchState.Completed && x.PendingCorrectionId is null && x.WinnerTeamId != null).ToArray();
        var wins = seeds.ToDictionary(id => id, id => completed.Count(x => x.WinnerTeamId == id));
        var directory = matches.ToDictionary(x => x.Id);
        var depths = new Dictionary<Guid, int>();
        int Depth(LiveSoloMatch match)
        {
            if (depths.TryGetValue(match.Id, out var depth)) return depth;
            return depths[match.Id] = 1 + match.Slots.Where(s => s.SourceMatchId != null && directory.ContainsKey(s.SourceMatchId.Value))
                .Select(s => Depth(directory[s.SourceMatchId!.Value])).DefaultIfEmpty(0).Max();
        }
        var champion = Champion(matches, stage.Format);
        var rows = seeds.Select((id, index) => {
            var losses = completed.Count(x => LiveSoloBracketPolicy.Loser(x) == id);
            var byes = completed.Count(x => x.WinnerTeamId == id && x.Slots.Count(s => s.TeamId != null) == 1);
            var opponents = completed.Where(x => x.Slots.Any(s => s.TeamId == id)).SelectMany(x => x.Slots)
                .Where(s => s.TeamId != null && s.TeamId != id).Select(s => s.TeamId!.Value);
            var state = stage.Format == LiveSoloBracketFormat.Swiss
                ? wins[id] >= stage.SwissAdvanceWins ? LiveSoloTournamentStandingState.Qualified
                    : losses >= stage.SwissEliminateLosses ? LiveSoloTournamentStandingState.Eliminated : LiveSoloTournamentStandingState.Active
                : id == champion ? LiveSoloTournamentStandingState.Qualified
                    : losses >= (stage.Format == LiveSoloBracketFormat.DoubleElimination ? 2 : 1)
                        ? LiveSoloTournamentStandingState.Eliminated : LiveSoloTournamentStandingState.Active;
            return new LiveSoloTournamentStanding(id, index + 1, wins[id], losses, opponents.Sum(opponent => wins.GetValueOrDefault(opponent)), byes, state);
        });
        return stage.Format == LiveSoloBracketFormat.Swiss
            ? rows.OrderByDescending(x => x.Wins).ThenBy(x => x.Losses).ThenByDescending(x => x.Buchholz).ThenBy(x => x.Seed).ToArray()
            : rows.OrderByDescending(x => x.TeamId == champion).ThenByDescending(x => completed.Where(m => LiveSoloBracketPolicy.Loser(m) == x.TeamId)
                .Select(Depth).DefaultIfEmpty(0).Max()).ThenBy(x => x.Seed).ToArray();
    }

    public static IReadOnlyList<Guid> Qualifiers(IReadOnlyList<LiveSoloMatch> matches, LiveSoloTournamentStage stage)
    {
        var ranked = Standings(matches, stage);
        return (stage.Format == LiveSoloBracketFormat.Swiss ? ranked.Where(x => x.State == LiveSoloTournamentStandingState.Qualified) : ranked)
            .Take(stage.AdvanceCount == 0 ? int.MaxValue : stage.AdvanceCount).Select(x => x.TeamId).ToArray();
    }

    public static IReadOnlyList<LiveSoloMatch> SwissRound(Guid competitionId, IReadOnlyList<Guid> seeds, IReadOnlyList<LiveSoloMatch> previous,
        LiveSoloTournamentStage stage, IReadOnlyList<LiveSoloStageRule> rules, int stageNumber, DateTimeOffset now)
    {
        var ranked = previous.Count == 0 ? seeds.Select((id, i) => new LiveSoloTournamentStanding(id, i + 1, 0, 0, 0, 0,
            LiveSoloTournamentStandingState.Active)).ToArray() : Standings(previous, stage).Where(x => x.State == LiveSoloTournamentStandingState.Active).ToArray();
        var round = previous.Select(x => x.Stage).DefaultIfEmpty(0).Max() + 1;
        var waiting = ranked.ToList();
        var pairs = new List<(LiveSoloTournamentStanding Left, LiveSoloTournamentStanding? Right)>();
        if (waiting.Count % 2 != 0)
        {
            var bye = waiting.OrderBy(x => x.Byes).ThenBy(x => x.Wins).ThenByDescending(x => x.Losses).ThenByDescending(x => x.Seed).First();
            waiting.Remove(bye); pairs.Add((bye, null));
        }
        static (Guid, Guid) Pair(Guid left, Guid right) => left.CompareTo(right) < 0 ? (left, right) : (right, left);
        var meetings = previous.Where(x => x.Slots.Count == 2 && x.Slots.All(s => s.TeamId != null))
            .GroupBy(x => Pair(x.Slots[0].TeamId!.Value, x.Slots[1].TeamId!.Value)).ToDictionary(x => x.Key, x => x.Count());
        while (waiting.Count > 0)
        {
            var left = waiting[0]; waiting.RemoveAt(0);
            var right = waiting.OrderBy(x => meetings.GetValueOrDefault(Pair(left.TeamId, x.TeamId)))
                .ThenBy(x => Math.Abs(left.Wins - x.Wins) + Math.Abs(left.Losses - x.Losses))
                .ThenBy(x => previous.Count == 0 ? Math.Abs(x.Seed - (left.Seed + (seeds.Count + 1) / 2)) : x.Seed).First();
            waiting.Remove(right); pairs.Add((left, right));
        }
        return pairs.Select((pair, position) => {
            var deciding = Math.Max(pair.Left.Wins, pair.Right?.Wins ?? 0) >= stage.SwissAdvanceWins - 1;
            var elimination = Math.Max(pair.Left.Losses, pair.Right?.Losses ?? 0) >= stage.SwissEliminateLosses - 1;
            var required = rules.SingleOrDefault(x => x.TournamentStage == stageNumber && x.Lane == LiveSoloBracketLane.Swiss && x.Stage == round)?.RequiredWins
                ?? (deciding && elimination ? Math.Max(stage.SwissQualificationRequiredWins, stage.SwissEliminationRequiredWins)
                    : deciding ? stage.SwissQualificationRequiredWins : elimination ? stage.SwissEliminationRequiredWins : stage.RequiredWins);
            var match = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = competitionId, TournamentStage = stageNumber,
                Lane = LiveSoloBracketLane.Swiss, Stage = round, Position = position, RequiredWins = required,
                SwissWins = pair.Left.Wins, SwissLosses = pair.Left.Losses, State = pair.Right is null ? LiveSoloMatchState.Completed : LiveSoloMatchState.Preparing,
                WinnerTeamId = pair.Right is null ? pair.Left.TeamId : null, CompletedAt = pair.Right is null ? now : null, CreatedAt = now };
            match.Slots = [new() { MatchId = match.Id, Side = LiveSoloSide.Left, Source = LiveSoloSlotSource.Seed,
                TeamId = pair.Left.TeamId, Seed = pair.Left.Seed, Resolved = true },
                new() { MatchId = match.Id, Side = LiveSoloSide.Right, Source = pair.Right is null ? LiveSoloSlotSource.Bye : LiveSoloSlotSource.Seed,
                    TeamId = pair.Right?.TeamId, Seed = pair.Right?.Seed, Resolved = true }];
            if (pair.Right is null) NoCTF.Application.LiveSolo.Rounds.LiveSoloResults.Confirm(match, LiveSoloEndReason.Bye, now);
            return match;
        }).ToArray();
    }
}

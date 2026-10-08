using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Brackets;

/// <summary>Pure seeded graph generation and source resolution. Byes never create a defeat.</summary>
public static class LiveSoloBracketPolicy
{
    public static IReadOnlyList<LiveSoloMatch> Generate(Guid competitionId, IReadOnlyList<Guid> seeds,
        LiveSoloCompetitionModeConfiguration configuration, DateTimeOffset now)
    {
        if (!Enum.IsDefined(configuration.BracketFormat)) throw new ArgumentOutOfRangeException(nameof(configuration));
        if (seeds.Count is < 2 or > 1024 || seeds.Contains(Guid.Empty) || seeds.Distinct().Count() != seeds.Count)
            throw new ArgumentException("A bracket requires 2..1024 distinct teams.", nameof(seeds));
        var size = 2; while (size < seeds.Count) size *= 2;
        var order = new[] { 1, 2 };
        for (var n = 4; n <= size; n *= 2) order = order.SelectMany(x => new[] { x, n + 1 - x }).ToArray();
        var all = new List<LiveSoloMatch>(); var winners = new List<LiveSoloMatch[]>();
        LiveSoloMatch Add(LiveSoloBracketLane lane, int stage, int position, LiveSoloMatchSlot left, LiveSoloMatchSlot right, bool conditional = false)
        {
            var required = configuration.StageRules.SingleOrDefault(x => x.Lane == lane && x.Stage == stage)?.RequiredWins ?? configuration.RequiredWins;
            if (required is < 1 or > 1024) throw new ArgumentException("Invalid required wins.", nameof(configuration));
            var match = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = competitionId, Lane = lane, Stage = stage, Position = position,
                RequiredWins = required, State = LiveSoloMatchState.AwaitingOpponents, Conditional = conditional, CreatedAt = now, Slots = [left, right] };
            left.MatchId = right.MatchId = match.Id; left.Side = LiveSoloSide.Left; right.Side = LiveSoloSide.Right; all.Add(match); return match;
        }
        LiveSoloMatchSlot Seed(int rank) => rank <= seeds.Count
            ? new() { Source = LiveSoloSlotSource.Seed, Seed = rank, TeamId = seeds[rank - 1], Resolved = true }
            : new() { Source = LiveSoloSlotSource.Bye, Resolved = true };
        LiveSoloMatchSlot Source(LiveSoloMatch match, LiveSoloSlotSource source) => new() { Source = source, SourceMatchId = match.Id };
        var first = Enumerable.Range(0, size / 2).Select(i => Add(LiveSoloBracketLane.Winners, 1, i, Seed(order[2 * i]), Seed(order[2 * i + 1]))).ToArray();
        winners.Add(first);
        for (var stage = 2; winners[^1].Length > 1; stage++)
        {
            var previous = winners[^1];
            winners.Add(Enumerable.Range(0, previous.Length / 2).Select(i => Add(LiveSoloBracketLane.Winners, stage, i,
                Source(previous[2 * i], LiveSoloSlotSource.Winner), Source(previous[2 * i + 1], LiveSoloSlotSource.Winner))).ToArray());
        }
        if (configuration.BracketFormat == LiveSoloBracketFormat.DoubleElimination)
        {
            LiveSoloMatchSlot loserChampion;
            if (size == 2) loserChampion = Source(first[0], LiveSoloSlotSource.Loser);
            else
            {
                var previous = Enumerable.Range(0, first.Length / 2).Select(i => Add(LiveSoloBracketLane.Losers, 1, i,
                    Source(first[2 * i], LiveSoloSlotSource.Loser), Source(first[2 * i + 1], LiveSoloSlotSource.Loser))).ToArray();
                for (var w = 1; w < winners.Count; w++)
                {
                    var drop = winners[w]; var input = previous;
                    previous = Enumerable.Range(0, drop.Length).Select(i => Add(LiveSoloBracketLane.Losers, 2 * w, i,
                        Source(input[i], LiveSoloSlotSource.Winner), Source(drop[drop.Length > 1 ? i ^ 1 : 0], LiveSoloSlotSource.Loser))).ToArray();
                    if (w == winners.Count - 1) continue;
                    input = previous;
                    previous = Enumerable.Range(0, input.Length / 2).Select(i => Add(LiveSoloBracketLane.Losers, 2 * w + 1, i,
                        Source(input[2 * i], LiveSoloSlotSource.Winner), Source(input[2 * i + 1], LiveSoloSlotSource.Winner))).ToArray();
                }
                loserChampion = Source(previous.Single(), LiveSoloSlotSource.Winner);
            }
            var final = Add(LiveSoloBracketLane.GrandFinal, 1, 0, Source(winners[^1].Single(), LiveSoloSlotSource.Winner), loserChampion);
            Add(LiveSoloBracketLane.ResetFinal, 1, 0, Source(final, LiveSoloSlotSource.Winner), Source(final, LiveSoloSlotSource.Loser), conditional: true);
        }
        Resolve(all, now); return all;
    }

    public static IReadOnlyList<Guid> Resolve(IReadOnlyList<LiveSoloMatch> matches, DateTimeOffset now)
    {
        var directory = matches.ToDictionary(x => x.Id); var changed = new HashSet<Guid>(); var again = true;
        while (again)
        {
            again = false;
            foreach (var match in matches.Where(x => x.State == LiveSoloMatchState.AwaitingOpponents))
            {
                if (match.Conditional && match.Lane == LiveSoloBracketLane.ResetFinal)
                {
                    var final = directory[match.Slots[0].SourceMatchId!.Value];
                    if (final.State != LiveSoloMatchState.Completed) continue;
                    if (final.WinnerTeamId != final.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId)
                    { match.State = LiveSoloMatchState.Canceled; match.CompletedAt = now; changed.Add(match.Id); again = true; continue; }
                }
                foreach (var slot in match.Slots.Where(x => !x.Resolved && x.SourceMatchId != null))
                {
                    var source = directory[slot.SourceMatchId!.Value];
                    if (source.State != LiveSoloMatchState.Completed) continue;
                    slot.TeamId = slot.Source == LiveSoloSlotSource.Winner ? source.WinnerTeamId : Loser(source);
                    slot.Resolved = true; changed.Add(match.Id); again = true;
                }
                if (match.Slots.Any(x => !x.Resolved)) continue;
                var teams = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
                if (teams.Length == 2)
                {
                    if (teams[0] == teams[1]) throw new InvalidOperationException("A team cannot play itself.");
                    match.State = LiveSoloMatchState.Preparing;
                }
                else
                {
                    match.State = LiveSoloMatchState.Completed; match.WinnerTeamId = teams.Length == 1 ? teams[0] : null;
                    match.CompletedAt = now;
                }
                changed.Add(match.Id); again = true;
            }
        }
        foreach (var id in changed) directory[id].ConcurrencyStamp = Guid.NewGuid();
        return changed.ToArray();
    }

    public static Guid? Loser(LiveSoloMatch match) => match.State != LiveSoloMatchState.Completed || match.WinnerTeamId is not Guid winner
        || match.Slots.Count(x => x.TeamId != null) != 2 ? null : match.Slots.Single(x => x.TeamId != winner).TeamId;

    public static int Defeats(Guid teamId, IEnumerable<LiveSoloMatch> matches) => matches.Count(x => Loser(x) == teamId);
}

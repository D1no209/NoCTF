using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloBracketPolicyTests
{
    [Test]
    [Arguments(2)] [Arguments(3)] [Arguments(5)] [Arguments(8)] [Arguments(16)]
    public async Task Single_elimination_advances_byes_without_creating_defeats_and_every_nonchampion_loses_once(int count)
    {
        var teams = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray(); var now = DateTimeOffset.UtcNow;
        var graph = LiveSoloBracketPolicy.Generate(Guid.NewGuid(), teams, new() { BracketFormat = LiveSoloBracketFormat.SingleElimination }, now);
        var firstByeWins = graph.Where(x => x.State == LiveSoloMatchState.Completed).ToArray();
        await Assert.That(firstByeWins.All(x => LiveSoloBracketPolicy.Loser(x) is null)).IsTrue();
        Play(graph, false, now);
        var final = graph.OrderByDescending(x => x.Stage).Single(x => x.Stage == graph.Max(m => m.Stage));
        await Assert.That(final.State).IsEqualTo(LiveSoloMatchState.Completed);
        await Assert.That(LiveSoloBracketPolicy.Defeats(final.WinnerTeamId!.Value, graph)).IsEqualTo(0);
        await Assert.That(teams.Where(x => x != final.WinnerTeamId).All(x => LiveSoloBracketPolicy.Defeats(x, graph) == 1)).IsTrue();
        await Assert.That(graph.Count(x => LiveSoloBracketPolicy.Loser(x) != null)).IsEqualTo(count - 1);
    }

    [Test]
    [Arguments(2, false)] [Arguments(3, false)] [Arguments(5, false)] [Arguments(8, false)] [Arguments(16, false)]
    [Arguments(2, true)] [Arguments(3, true)] [Arguments(5, true)] [Arguments(8, true)] [Arguments(16, true)]
    public async Task Double_elimination_requires_two_real_defeats_and_only_activates_the_reset_final_when_needed(int count, bool reset)
    {
        var teams = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray(); var now = DateTimeOffset.UtcNow;
        var graph = LiveSoloBracketPolicy.Generate(Guid.NewGuid(), teams, new() { BracketFormat = LiveSoloBracketFormat.DoubleElimination }, now);
        Play(graph, reset, now);
        var conditional = graph.Single(x => x.Lane == LiveSoloBracketLane.ResetFinal);
        await Assert.That(conditional.State).IsEqualTo(reset ? LiveSoloMatchState.Completed : LiveSoloMatchState.Canceled);
        var champion = (reset ? conditional : graph.Single(x => x.Lane == LiveSoloBracketLane.GrandFinal)).WinnerTeamId!.Value;
        await Assert.That(LiveSoloBracketPolicy.Defeats(champion, graph)).IsEqualTo(reset ? 1 : 0);
        await Assert.That(teams.Where(x => x != champion).All(x => LiveSoloBracketPolicy.Defeats(x, graph) == 2)).IsTrue();
        await Assert.That(graph.Count(x => LiveSoloBracketPolicy.Loser(x) != null)).IsEqualTo(2 * count - (reset ? 1 : 2));
        await Assert.That(graph.Any(x => x.State is LiveSoloMatchState.Preparing or LiveSoloMatchState.AwaitingOpponents)).IsFalse();
    }

    [Test]
    public async Task Stage_overrides_and_unresolved_sources_do_not_invent_opponents()
    {
        var graph = LiveSoloBracketPolicy.Generate(Guid.NewGuid(), Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray(), new() {
            RequiredWins = 2, BracketFormat = LiveSoloBracketFormat.DoubleElimination, StageRules = [
                new() { Lane = LiveSoloBracketLane.Winners, Stage = 1, RequiredWins = 1 },
                new() { Lane = LiveSoloBracketLane.GrandFinal, Stage = 1, RequiredWins = 3 }] }, DateTimeOffset.UtcNow);
        await Assert.That(graph.Where(x => x.Lane == LiveSoloBracketLane.Winners && x.Stage == 1).All(x => x.RequiredWins == 1)).IsTrue();
        await Assert.That(graph.Single(x => x.Lane == LiveSoloBracketLane.GrandFinal).RequiredWins).IsEqualTo(3);
        await Assert.That(graph.Where(x => x.State == LiveSoloMatchState.AwaitingOpponents).All(x => x.Slots.Any(s => !s.Resolved))).IsTrue();
    }

    private static void Play(IReadOnlyList<LiveSoloMatch> graph, bool reset, DateTimeOffset now)
    {
        for (var step = 0; step < graph.Count * 2; step++)
        {
            var match = graph.Where(x => x.State == LiveSoloMatchState.Preparing).OrderBy(x => x.Lane).ThenBy(x => x.Stage).ThenBy(x => x.Position).FirstOrDefault();
            if (match is null) break;
            var side = reset && match.Lane == LiveSoloBracketLane.GrandFinal ? LiveSoloSide.Right : LiveSoloSide.Left;
            match.WinnerTeamId = match.Slots.Single(x => x.Side == side).TeamId;
            match.State = LiveSoloMatchState.Completed; match.StartedAt = match.CompletedAt = now;
            LiveSoloBracketPolicy.Resolve(graph, now);
        }
    }
}

using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloRoundRulesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
    [Test]
    public async Task Ordered_resolution_keeps_gaps_and_unresolved_predecessors_blocking_later_winners()
    {
        var winner = new LiveSoloOrderedResult(3, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Correct);
        foreach (var state in new[] { GameplayFactState.Queued, GameplayFactState.PlatformFailed })
        {
            var resolution = new LiveSoloOrderedResolution(3);
            resolution.Observe(new(1, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Wrong));
            resolution.Observe(new(2, Guid.NewGuid(), Guid.NewGuid(), state, null));
            resolution.Observe(winner);
            await Assert.That(resolution.Decision).IsEqualTo(new LiveSoloResolutionDecision(LiveSoloResolution.Waiting, 1));
        }
        var gap = new LiveSoloOrderedResolution(3);
        gap.Observe(new(1, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Wrong));
        gap.Observe(winner);
        await Assert.That(gap.Decision).IsEqualTo(new LiveSoloResolutionDecision(LiveSoloResolution.Waiting, 1));
    }

    [Test]
    public async Task Ordered_resolution_drains_long_wrong_prefix_and_keeps_the_first_winner()
    {
        var resolution = new LiveSoloOrderedResolution(1002);
        for (var sequence = 1; sequence <= 1000; sequence++)
            resolution.Observe(new(sequence, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Wrong));
        await Assert.That(resolution.Decision).IsEqualTo(new LiveSoloResolutionDecision(LiveSoloResolution.Waiting, 1000));
        var first = new LiveSoloOrderedResult(1001, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Correct);
        resolution.Observe(first);
        resolution.Observe(new(1002, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Correct));
        await Assert.That(resolution.Decision).IsEqualTo(new LiveSoloResolutionDecision(
            LiveSoloResolution.Winner, 1001, first.TeamId, first.GameplayFactId));
        var empty = new LiveSoloOrderedResolution(0);
        await Assert.That(empty.Decision).IsEqualTo(new LiveSoloResolutionDecision(LiveSoloResolution.NoWinner, 0));
    }

    [Test]
    public async Task An_earlier_pending_or_failed_dependency_prevents_a_later_correct_flag_stealing_the_round()
    {
        var later = new LiveSoloOrderedResult(2, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Correct);
        foreach (var state in new[] { GameplayFactState.Queued, GameplayFactState.Processing, GameplayFactState.PlatformFailed })
        {
            var first = new LiveSoloOrderedResult(1, Guid.NewGuid(), Guid.NewGuid(), state, null);
            await Assert.That(LiveSoloRoundRules.Resolve(2, [later, first]).State).IsEqualTo(LiveSoloResolution.Waiting);
        }
        var wrong = new LiveSoloOrderedResult(1, Guid.NewGuid(), Guid.NewGuid(), GameplayFactState.Completed, GameplayFactResult.Wrong);
        await Assert.That(LiveSoloRoundRules.Resolve(2, [later, wrong]).WinningGameplayFactId).IsEqualTo(later.GameplayFactId);
    }
    [Test]
    public async Task Both_teams_and_all_questions_share_one_order_and_winning_is_idempotent()
    {
        var left = Guid.NewGuid(); var right = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var match = new LiveSoloMatch { Id = Guid.NewGuid(), RequiredWins = 1, State = LiveSoloMatchState.Running,
            Slots = [new() { Side = LiveSoloSide.Left, TeamId = left }, new() { Side = LiveSoloSide.Right, TeamId = right }] };
        var round = new LiveSoloRound { Id = Guid.NewGuid(), MatchId = match.Id, State = LiveSoloRoundState.Running, StartedAt = Start };
        var result = LiveSoloRoundRules.Resolve(2, [new(2, b, right, GameplayFactState.Completed, GameplayFactResult.Correct),
            new(1, a, left, GameplayFactState.Completed, GameplayFactResult.Correct)]);
        LiveSoloRoundRules.ApplyWinner(match, round, result, Start.AddSeconds(1));
        LiveSoloRoundRules.ApplyWinner(match, round, result, Start.AddSeconds(2));
        await Assert.That(match.LeftWins).IsEqualTo(1); await Assert.That(match.RightWins).IsEqualTo(0);
        await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Completed);
        await Assert.That(round.WinningGameplayFactId).IsEqualTo(a);
    }
    [Test]
    public async Task Admission_has_an_exclusive_deadline_and_earlier_open_questions_remain_valid()
    {
        var round = new LiveSoloRound { Id = Guid.NewGuid(), State = LiveSoloRoundState.Running, StartedAt = Start, LimitSeconds = 10 };
        var first = new LiveSoloRoundQuestion { Id = Guid.NewGuid(), RoundId = round.Id, OpenedAt = Start };
        await Assert.That(LiveSoloRoundRules.CanAdmit(round, first, Start.AddSeconds(9))).IsNull();
        await Assert.That(LiveSoloRoundRules.CanAdmit(round, first, Start.AddSeconds(10))).IsEqualTo(LiveSoloFailure.DeadlinePassed);
        round.Pauses.Add(new() { StartedAt = Start.AddSeconds(5), EndedAt = Start.AddSeconds(8) });
        await Assert.That(LiveSoloRoundRules.CanAdmit(round, first, Start.AddSeconds(11))).IsNull();
        round.Pauses.Add(new() { StartedAt = Start.AddSeconds(10) });
        await Assert.That(LiveSoloRoundRules.CanAdmit(round, first, Start.AddSeconds(11))).IsEqualTo(LiveSoloFailure.RoundPaused);
    }
    [Test]
    public async Task Timeout_drains_admitted_work_and_voids_without_adding_wins()
    {
        var round = new LiveSoloRound { Id = Guid.NewGuid(), State = LiveSoloRoundState.Running, StartedAt = Start, LimitSeconds = 10 };
        await Assert.That(LiveSoloRoundRules.TryVoid(round, new(LiveSoloResolution.Waiting, 0), Start.AddSeconds(11))).IsFalse();
        await Assert.That(LiveSoloRoundRules.TryVoid(round, new(LiveSoloResolution.NoWinner, 1), Start.AddSeconds(11))).IsTrue();
        await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.TimedOut);
        await Assert.That(round.WinnerTeamId).IsNull();
    }
}

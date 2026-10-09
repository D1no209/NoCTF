using NSubstitute;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloAdjudicationPolicyTests
{
    [Test]
    public async Task Pause_and_resume_preserve_the_round_phase_and_union_with_competition_pauses()
    {
        var now = DateTimeOffset.UtcNow;
        var (match, round) = Active(now);
        LiveSoloAdjudicationPolicy.Apply(match, round, Command(match, round, LiveSoloJudgeAction.Pause), now.AddSeconds(5));
        await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Paused);
        await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.Running);
        round.Pauses.Add(new() { Source = LiveSoloPauseSource.Competition, StartedAt = now.AddSeconds(10), EndedAt = now.AddSeconds(30) });
        var decision = LiveSoloAdjudicationPolicy.Apply(match, round, Command(match, round, LiveSoloJudgeAction.Resume), now.AddSeconds(20));
        await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Running);
        await Assert.That(LiveSoloActiveClock.Elapsed(now, now.AddSeconds(40), round.Pauses)).IsEqualTo(TimeSpan.FromSeconds(15));
        await Assert.That(decision.PreviousMatchState).IsEqualTo(LiveSoloMatchState.Paused);
        await Assert.That(decision.MatchState).IsEqualTo(LiveSoloMatchState.Running);
    }

    [Test, Arguments(LiveSoloJudgeAction.Pause), Arguments(LiveSoloJudgeAction.Resume),
        Arguments(LiveSoloJudgeAction.VoidRound), Arguments(LiveSoloJudgeAction.ForfeitMatch)]
    public async Task All_actions_reject_a_stale_match_round_or_timeline_revision(LiveSoloJudgeAction action)
    {
        var (match, round) = Active(DateTimeOffset.UtcNow);
        var command = Command(match, round, action);
        await Assert.That(LiveSoloAdjudicationPolicy.CanApply(match, round, command with { ExpectedMatchStamp = Guid.NewGuid() })).IsEqualTo(LiveSoloFailure.Conflict);
        await Assert.That(LiveSoloAdjudicationPolicy.CanApply(match, round, command with { ExpectedRoundId = Guid.NewGuid() })).IsEqualTo(LiveSoloFailure.Conflict);
        await Assert.That(LiveSoloAdjudicationPolicy.CanApply(match, round, command with { ExpectedRoundStamp = Guid.NewGuid() })).IsEqualTo(LiveSoloFailure.Conflict);
        await Assert.That(LiveSoloAdjudicationPolicy.CanApply(match, round, command with { ExpectedTimelineRevision = round.TimelineRevision + 1 })).IsEqualTo(LiveSoloFailure.Conflict);
    }

    [Test]
    public async Task Voiding_preserves_previous_wins_and_forfeit_does_not_invent_round_wins_or_a_correct_flag()
    {
        var now = DateTimeOffset.UtcNow; var (match, round) = Active(now); match.LeftWins = 1; match.RequiredWins = 2;
        LiveSoloAdjudicationPolicy.Apply(match, round, Command(match, round, LiveSoloJudgeAction.VoidRound), now);
        await Assert.That(round.State).IsEqualTo(LiveSoloRoundState.Canceled);
        await Assert.That(match.LeftWins).IsEqualTo(1);
        await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Preparing);
        LiveSoloAdjudicationPolicy.Apply(match, round, Command(match, round, LiveSoloJudgeAction.ForfeitMatch), now);
        await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Completed);
        await Assert.That(match.WinnerTeamId).IsEqualTo(match.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId);
        await Assert.That(match.LeftWins).IsEqualTo(1); await Assert.That(match.RightWins).IsEqualTo(0);
        await Assert.That(round.WinningGameplayFactId).IsNull(); await Assert.That(round.WinnerTeamId).IsNull();
    }

    [Test]
    public async Task Missing_reason_or_incomplete_revision_context_never_reaches_the_store()
    {
        var store = Substitute.For<ILiveSoloAdjudicationStore>(); var service = new ManageLiveSoloAdjudication(store);
        var (match, round) = Active(DateTimeOffset.UtcNow); var command = Command(match, round, LiveSoloJudgeAction.Pause);
        foreach (var invalid in new[] { command with { Reason = "  " }, command with { Reason = new string('x', 4001) },
            command with { ExpectedRoundStamp = null }, command with { ExpectedTimelineRevision = null },
            command with { Action = (LiveSoloJudgeAction)100 }, command with { ForfeitingTeamId = Guid.NewGuid() } })
            await Assert.That((await service.ApplyAsync(invalid, CancellationToken.None)).Failure).IsEqualTo(LiveSoloFailure.InvalidConfiguration);
        await store.DidNotReceive().ApplyAsync(Arg.Any<AdjudicateLiveSoloMatch>(), Arg.Any<CancellationToken>());
    }

    internal static (LiveSoloMatch Match, LiveSoloRound Round) Active(DateTimeOffset now)
    {
        var match = new LiveSoloMatch { Id = Guid.NewGuid(), State = LiveSoloMatchState.Running,
            Slots = [new() { Side = LiveSoloSide.Left, TeamId = Guid.NewGuid(), Resolved = true },
                new() { Side = LiveSoloSide.Right, TeamId = Guid.NewGuid(), Resolved = true }] };
        var round = new LiveSoloRound { Id = Guid.NewGuid(), MatchId = match.Id, State = LiveSoloRoundState.Running, StartedAt = now };
        match.CurrentRoundId = round.Id; return (match, round);
    }
    internal static AdjudicateLiveSoloMatch Command(LiveSoloMatch match, LiveSoloRound? round, LiveSoloJudgeAction action) =>
        new(match.CompetitionId, match.Id, Guid.NewGuid(), match.ConcurrencyStamp, round?.Id, round?.ConcurrencyStamp, round?.TimelineRevision,
            action, action == LiveSoloJudgeAction.ForfeitMatch ? match.Slots.Single(x => x.Side == LiveSoloSide.Left).TeamId : null, "Judge reason");
}

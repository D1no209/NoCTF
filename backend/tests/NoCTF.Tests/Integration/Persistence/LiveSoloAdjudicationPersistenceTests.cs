using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloAdjudicationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Judge_pause_resume_overlaps_competition_pause_and_staff_history_is_read_only_for_observers(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var started = fixture.Now;
            fixture.Competition.Collaborators.Add(new() { UserId = fixture.Left.Id, Role = CompetitionCollaboratorRole.Observer });
            await fixture.Db.SaveChangesAsync(ct);
            var store = fixture.Store(fixture.Db);
            var pause = await CommandAsync(fixture, LiveSoloJudgeAction.Pause, ct);
            await Assert.That((await store.ApplyAsync(pause with { ActorId = fixture.Left.Id }, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            fixture.Now = started.AddSeconds(5);
            var paused = await store.ApplyAsync(pause, ct); await Assert.That(paused.Failure).IsNull();
            await Assert.That((await store.AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                fixture.Round.Questions.Single(x => x.Position == 0).Id, fixture.Right.Id, "flag{first}", fixture.Now), ct)).Failure).IsEqualTo(LiveSoloFailure.RoundPaused);
            void Transition(DateTimeOffset at, CompetitionStatus from, CompetitionStatus to)
            {
                var item = CompetitionEventGeneratedCatalog.Create(CompetitionEventKind.CompetitionLifecycleChanged);
                item.Id = Guid.NewGuid(); item.CompetitionId = fixture.Competition.Id; item.OccurredAt = at;
                item.PreviousCompetitionStatus = from; item.CompetitionStatus = to; fixture.Db.CompetitionEvents.Add(item);
            }
            Transition(started.AddSeconds(10), CompetitionStatus.Running, CompetitionStatus.Paused);
            Transition(started.AddSeconds(30), CompetitionStatus.Paused, CompetitionStatus.Running);
            fixture.Competition.Collaborators.Single(x => x.UserId == fixture.Left.Id).Role = CompetitionCollaboratorRole.Judge;
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = started.AddSeconds(35);
            var resumed = await store.ApplyAsync((await CommandAsync(fixture, LiveSoloJudgeAction.Resume, ct)) with { ActorId = fixture.Left.Id }, ct);
            await Assert.That(resumed.Failure).IsNull();
            await Assert.That(resumed.Round!.ActiveElapsedMilliseconds).IsEqualTo(5_000);
            await Assert.That(resumed.Round.Paused).IsFalse();
            await Assert.That((await store.ReadAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, ct))!.Count).IsEqualTo(2);
            await Assert.That(await store.ReadAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Right.Id, ct)).IsNull();
            await using var restarted = new NoCtfDbContext(fixture.Options);
            await Assert.That((await new LiveSoloScheduleSource(restarted).RebuildAsync(fixture.Now, ct)).Count).IsEqualTo(1);
            var decision = await fixture.Db.LiveSoloAdjudications.FirstAsync(ct);
            decision.Reason = "silently edited";
            await Assert.That(async () => await fixture.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Concurrent_decisions_for_the_same_revision_commit_only_one_pause_and_one_audit_record(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var command = await CommandAsync(fixture, LiveSoloJudgeAction.Pause, ct);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await fixture.Store(db).ApplyAsync(command, ct);
            }));
            await Assert.That(results.Count(x => x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x => x.Failure == LiveSoloFailure.Conflict)).IsEqualTo(1);
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That(await read.LiveSoloAdjudications.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await read.Set<LiveSoloPauseInterval>().CountAsync(ct)).IsEqualTo(1);
            await Assert.That((await read.LiveSoloMatches.SingleAsync(ct)).State).IsEqualTo(LiveSoloMatchState.Paused);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Void_rejects_unfinished_admissions_and_stops_resources_without_a_win(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var accepted = await fixture.Store(fixture.Db).AdmitAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id,
                fixture.Round.Questions.Single(x => x.Position == 0).Id, fixture.Left.Id, "flag{first}", fixture.Now), ct);
            await Assert.That(accepted.Failure).IsNull();
            var preparation = Substitute.For<ILiveSoloRuntimePreparation>();
            var store = new LiveSoloMatchStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), fixture.media, fixture.egress,
                preparation, Substitute.For<IPostCommitMessagePublisher>(), clock: fixture.Clock);
            var result = await store.ApplyAsync(await CommandAsync(fixture, LiveSoloJudgeAction.VoidRound, ct), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Round!.State).IsEqualTo(LiveSoloRoundState.Canceled);
            await Assert.That(result.Match!.LeftWins).IsEqualTo(0); await Assert.That(result.Match.RightWins).IsEqualTo(0);
            await preparation.Received(1).StopRoundAsync(fixture.Round.Id, Arg.Any<DateTimeOffset>(), ct);
            await store.ResolveAsync(fixture.Round.Id, fixture.Now, ct);
            var fact = await fixture.Db.GameplayFacts.AsNoTracking().SingleAsync(ct);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Rejected);
            await Assert.That(fact.FailureCode).IsEqualTo(GameplayFactFailureCode.RoundOutOfRange);
            await Assert.That((await fixture.Db.LiveSoloRounds.AsNoTracking().SingleAsync(ct)).WinningGameplayFactId).IsNull();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Forfeit_advances_a_real_bracket_defeat_atomically_without_fabricated_gameplay_or_round_wins(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            ((LiveSoloCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).BracketFormat = LiveSoloBracketFormat.DoubleElimination;
            await fixture.Db.SaveChangesAsync(ct); var store = fixture.Store(fixture.Db);
            var graph = await store.GenerateAsync(new(fixture.Competition.Id, fixture.Owner.Id, fixture.Competition.ConcurrencyStamp,
                [fixture.LeftTeam.Id, fixture.RightTeam.Id], fixture.Now), ct);
            var first = graph.Bracket!.Matches.Single(x => x.Match.State == LiveSoloMatchState.Preparing).Match;
            var command = new AdjudicateLiveSoloMatch(fixture.Competition.Id, first.Id, fixture.Owner.Id, first.ConcurrencyStamp,
                null, null, null, LiveSoloJudgeAction.ForfeitMatch, first.LeftTeamId, "队伍主动弃权");
            var result = await store.ApplyAsync(command, ct);
            await Assert.That(result.Failure).IsNull(); await Assert.That(result.Match!.WinnerTeamId).IsEqualTo(first.RightTeamId);
            await Assert.That(result.Match.LeftWins).IsEqualTo(0); await Assert.That(result.Match.RightWins).IsEqualTo(0);
            await Assert.That((await store.ApplyAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
            await using var read = new NoCtfDbContext(fixture.Options);
            var final = await read.LiveSoloMatches.SingleAsync(x => x.Lane == LiveSoloBracketLane.GrandFinal, ct);
            await Assert.That(final.State).IsEqualTo(LiveSoloMatchState.Preparing);
            await Assert.That((await read.LiveSoloActiveTeamSlots.Select(x => x.MatchId).ToArrayAsync(ct)).All(x => x == final.Id)).IsTrue();
            await Assert.That(await read.LiveSoloAdjudications.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await read.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await read.LiveSoloRounds.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Commit_failure_rolls_back_decision_state_and_audit_and_does_not_stop_resources(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var command = await CommandAsync(fixture, LiveSoloJudgeAction.VoidRound, ct);
            await using var failing = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(fixture.Options).AddInterceptors(new RejectCommit()).Options);
            var preparation = Substitute.For<ILiveSoloRuntimePreparation>(); var messages = Substitute.For<IPostCommitMessagePublisher>();
            var store = new LiveSoloMatchStore(failing, new CompetitionModerationAuthorizer(failing), fixture.media, fixture.egress, preparation, messages, clock: fixture.Clock);
            await Assert.That(async () => await store.ApplyAsync(command, ct)).Throws<InvalidOperationException>();
            await preparation.DidNotReceive().StopRoundAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), ct);
            await messages.DidNotReceive().FlushCommittedMessagesAsync();
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That(await read.LiveSoloAdjudications.CountAsync(ct)).IsEqualTo(0);
            await Assert.That((await read.LiveSoloRounds.SingleAsync(ct)).State).IsEqualTo(LiveSoloRoundState.Running);
            await Assert.That((await read.LiveSoloMatches.SingleAsync(ct)).ConcurrencyStamp).IsEqualTo(command.ExpectedMatchStamp);
        });
    }

    private static async Task<AdjudicateLiveSoloMatch> CommandAsync(LiveSoloMatchPersistenceTests.Fixture fixture, LiveSoloJudgeAction action, CancellationToken ct)
    {
        var match = await fixture.Db.LiveSoloMatches.AsNoTracking().SingleAsync(x => x.Id == fixture.Match.Id, ct);
        var round = await fixture.Db.LiveSoloRounds.AsNoTracking().SingleAsync(x => x.Id == match.CurrentRoundId, ct);
        return new(fixture.Competition.Id, match.Id, fixture.Owner.Id, match.ConcurrencyStamp, round.Id, round.ConcurrencyStamp,
            round.TimelineRevision, action, action == LiveSoloJudgeAction.ForfeitMatch ? fixture.LeftTeam.Id : null, "裁判说明");
    }
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected commit failure");
    }
}

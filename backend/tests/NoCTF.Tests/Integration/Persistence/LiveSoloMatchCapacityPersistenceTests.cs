using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloMatchCapacityPersistenceTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task A_paused_initial_countdown_retains_its_slot_during_sequential_or_concurrent_start_and_resume(
        bool concurrent, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var (first, second) = await PrepareTwoAsync(fixture, 1, ct);
            await Assert.That((await fixture.Store(fixture.Db).StartCountdownAsync(first, ct)).Failure).IsNull();
            var pause = await DecisionAsync(fixture, first.MatchId, LiveSoloJudgeAction.Pause, ct);
            await Assert.That((await fixture.Store(fixture.Db).ApplyAsync(pause, ct)).Failure).IsNull();
            await using var before = new NoCtfDbContext(fixture.Options);
            var paused = await before.LiveSoloMatches.SingleAsync(x => x.Id == first.MatchId, ct);
            await Assert.That(paused.StartedAt).IsNull(); await Assert.That(paused.State).IsEqualTo(LiveSoloMatchState.Paused);
            var resume = await DecisionAsync(fixture, first.MatchId, LiveSoloJudgeAction.Resume, ct);
            async Task<LiveSoloRoundResult> StartSecond()
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await fixture.Store(db).StartCountdownAsync(second, ct);
            }
            async Task<LiveSoloAdjudicationResult> ResumeFirst()
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await fixture.Store(db).ApplyAsync(resume, ct);
            }
            if (concurrent)
            {
                var start = StartSecond(); var restart = ResumeFirst();
                await Task.WhenAll(start, restart);
                await Assert.That((await start).Failure).IsEqualTo(LiveSoloFailure.NotReady);
                await Assert.That((await restart).Failure).IsNull();
            }
            else
            {
                await Assert.That((await StartSecond()).Failure).IsEqualTo(LiveSoloFailure.NotReady);
                await Assert.That((await ResumeFirst()).Failure).IsNull();
            }
            await using var read = new NoCtfDbContext(fixture.Options);
            var round = await read.LiveSoloRounds.SingleAsync(x => x.Id == first.RoundId, ct);
            fixture.Now = fixture.Now.AddSeconds(round.CountdownSeconds);
            await fixture.Store(read).TickAsync(round.Id, round.TimelineRevision, fixture.Now, ct);
            await Assert.That((await read.LiveSoloMatches.SingleAsync(x => x.Id == first.MatchId, ct)).State).IsEqualTo(LiveSoloMatchState.Running);
            await Assert.That(await ReservedAsync(read, fixture.Competition.Id, ct)).IsEqualTo(1);
            await Assert.That((await read.LiveSoloMatches.SingleAsync(x => x.Id == second.MatchId, ct)).State).IsEqualTo(LiveSoloMatchState.Preparing);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Simultaneous_initial_countdowns_cannot_commit_two_reservations_under_a_limit_of_one(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var (first, second) = await PrepareTwoAsync(fixture, 1, ct);
            var results = await Task.WhenAll(new[] { first, second }.Select(async command =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await fixture.Store(db).StartCountdownAsync(command, ct);
            }));
            await Assert.That(results.Count(x => x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x => x.Failure is LiveSoloFailure.NotReady or LiveSoloFailure.Conflict)).IsEqualTo(1);
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That(await ReservedAsync(read, fixture.Competition.Id, ct)).IsEqualTo(1);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Resume_rechecks_a_lowered_limit_and_preserves_the_pause_and_audit_when_capacity_is_exceeded(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            var (first, second) = await PrepareTwoAsync(fixture, 2, ct);
            var store = fixture.Store(fixture.Db);
            await Assert.That((await store.StartCountdownAsync(first, ct)).Failure).IsNull();
            await Assert.That((await store.StartCountdownAsync(second, ct)).Failure).IsNull();
            await Assert.That((await store.ApplyAsync(await DecisionAsync(fixture, first.MatchId, LiveSoloJudgeAction.Pause, ct), ct)).Failure).IsNull();
            ((LiveSoloCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).MaximumConcurrentMatches = 1;
            await fixture.Db.SaveChangesAsync(ct);
            var command = await DecisionAsync(fixture, first.MatchId, LiveSoloJudgeAction.Resume, ct);
            await Assert.That((await store.ApplyAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.NotReady);
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That((await read.LiveSoloMatches.SingleAsync(x => x.Id == first.MatchId, ct)).State).IsEqualTo(LiveSoloMatchState.Paused);
            await Assert.That(await read.LiveSoloAdjudications.CountAsync(ct)).IsEqualTo(1);
            await Assert.That((await read.Set<LiveSoloPauseInterval>().SingleAsync(ct)).EndedAt).IsNull();
        });
    }

    private static Task<int> ReservedAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct) =>
        db.LiveSoloMatches.Where(x => x.CompetitionId == competitionId).Where(LiveSoloMatchCapacityPolicy.OccupiesSlot).CountAsync(ct);

    private static async Task<(StartLiveSoloCountdown First, StartLiveSoloCountdown Second)> PrepareTwoAsync(
        LiveSoloMatchPersistenceTests.Fixture fixture, int maximum, CancellationToken ct)
    {
        ((LiveSoloCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).MaximumConcurrentMatches = maximum;
        await fixture.Db.SaveChangesAsync(ct); await fixture.PrepareAsync(ct);
        var users = Enumerable.Range(0, 2).Select(i => new User { Id = Guid.NewGuid(), UserName = "extra" + i, Email = $"extra{i}@example.test",
            NormalizedUserName = "EXTRA" + i, PasswordHash = "unused", AccountStatus = UserAccountStatus.Active, CreatedAt = fixture.Now }).ToArray();
        var teams = users.Select((user, i) => new Team { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, CaptainId = user.Id,
            MemberIds = [user.Id], Name = user.UserName, InvitationToken = new string((char)('x' + i), 32),
            RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now }).ToArray();
        fixture.Db.Users.AddRange(users); fixture.Db.Teams.AddRange(teams); await fixture.Db.SaveChangesAsync(ct);
        var store = fixture.Store(fixture.Db);
        var created = await store.CreateAsync(new(fixture.Competition.Id, fixture.Owner.Id, teams[0].Id, teams[1].Id, null, fixture.Now), ct);
        await Assert.That(created.Failure).IsNull(); var match = created.Match!;
        for (var i = 0; i < 2; i++)
        {
            var roster = await store.LockRosterAsync(new(fixture.Competition.Id, match.Id, users[i].Id, teams[i].Id,
                match.ConcurrencyStamp, [users[i].Id], fixture.Now), ct);
            await Assert.That(roster.Failure).IsNull(); match = roster.Match!;
        }
        var prepared = await store.PrepareRoundAsync(new(fixture.Competition.Id, match.Id, fixture.Owner.Id, match.ConcurrencyStamp,
            fixture.Round.QuestionGroupId, fixture.Now), ct);
        await Assert.That(prepared.Failure).IsNull();
        fixture.Db.LiveSoloMediaSessions.Add(new() { Id = Guid.NewGuid(), MatchId = match.Id, Generation = Guid.NewGuid(),
            RoomIdentity = Guid.NewGuid().ToString("N"), State = LiveSoloMediaState.Ready, CreatedAt = fixture.Now,
            Participants = users.Select((user, i) => new LiveSoloMediaParticipant { UserId = user.Id, TeamId = teams[i].Id,
                Side = i == 0 ? LiveSoloSide.Left : LiveSoloSide.Right, Identity = user.Id.ToString("N"), ObservedAt = fixture.Now }).ToList() });
        await fixture.Db.SaveChangesAsync(ct);
        var identities = await fixture.Db.LiveSoloMediaParticipants.Select(x => x.Identity).ToArrayAsync(ct);
        fixture.media.ObserveAsync(Arg.Any<string>(), ct).Returns(new LiveSoloRoomObservation(identities.Select(id =>
            new LiveSoloObservedScreen(id, LiveSoloScreenState.Sharing, "track-" + id, fixture.Now)).ToArray()));
        async Task<StartLiveSoloCountdown> Ready(Guid id, Guid roundId)
        {
            var current = (await store.FindAsync(fixture.Competition.Id, id, fixture.Owner.Id, true, fixture.Now, ct))!;
            foreach (var roster in current.Rosters)
            {
                var ready = await store.ConfirmReadyAsync(fixture.Competition.Id, id, roster.UserIds.Single(), current.ConcurrencyStamp, fixture.Now, ct);
                await Assert.That(ready.Failure).IsNull(); current = ready.Match!;
            }
            var round = await fixture.Db.LiveSoloRounds.AsNoTracking().SingleAsync(x => x.Id == roundId, ct);
            return new(fixture.Competition.Id, id, round.Id, fixture.Owner.Id, round.ConcurrencyStamp, fixture.Now);
        }
        return (await Ready(fixture.Match.Id, fixture.Round.Id), await Ready(match.Id, prepared.Round!.Id));
    }

    private static async Task<AdjudicateLiveSoloMatch> DecisionAsync(LiveSoloMatchPersistenceTests.Fixture fixture, Guid matchId,
        LiveSoloJudgeAction action, CancellationToken ct)
    {
        var match = await fixture.Db.LiveSoloMatches.AsNoTracking().SingleAsync(x => x.Id == matchId, ct);
        var round = await fixture.Db.LiveSoloRounds.AsNoTracking().SingleAsync(x => x.Id == match.CurrentRoundId, ct);
        return new(fixture.Competition.Id, match.Id, fixture.Owner.Id, match.ConcurrencyStamp, round.Id, round.ConcurrencyStamp,
            round.TimelineRevision, action, null, "容量边界测试");
    }
}

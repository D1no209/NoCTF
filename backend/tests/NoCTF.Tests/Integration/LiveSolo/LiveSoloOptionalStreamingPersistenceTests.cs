using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Persistence.PostgreSql;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloOptionalStreamingPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task No_media_services_are_needed_for_readiness_countdown_questions_and_a_flag_win(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, platformStreaming: false);
            f.media.CheckAsync(ct).Returns(Task.FromException<LiveSoloMediaReadiness>(new InvalidOperationException("No media service")));
            await f.PrepareAsync(ct);
            await using (var fresh = new NoCtfDbContext(f.Options))
            {
                await Assert.That((await fresh.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).PlatformStreamingEnabled).IsFalse();
                await Assert.That((await fresh.LiveSoloRounds.SingleAsync(ct)).PlatformStreamingEnabled).IsFalse();
            }
            var store = f.Store(f.Db);
            var premature = await store.StartCountdownAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, f.Round.ConcurrencyStamp, f.Now), ct);
            await Assert.That(premature.Failure).IsEqualTo(LiveSoloFailure.NotReady);
            await f.StartAsync(ct);
            var question = f.Round.Questions.Single(x => x.Position == 0);
            var accepted = await store.AdmitAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, question.Id, f.Left.Id, "flag{first}", f.Now), ct);
            await Assert.That(accepted.Failure).IsNull();
            await store.ResolveAsync(f.Round.Id, f.Now, ct);
            await Assert.That((await f.Db.GameplayFacts.SingleAsync(x => x.Id == accepted.GameplayFactId, ct)).Result).IsEqualTo(GameplayFactResult.Correct);
            var result = await store.FindAsync(f.Competition.Id, f.Match.Id, f.Left.Id, false, f.Now, ct);
            await Assert.That(result!.State).IsEqualTo(LiveSoloMatchState.Completed);
            await Assert.That(result.WinnerTeamId).IsEqualTo(f.LeftTeam.Id);
            await Assert.That(result.HasMedia).IsFalse();
            await Assert.That(await f.Db.LiveSoloMediaSessions.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloProgramCaptures.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await f.Db.LiveSoloRecordings.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(f.media.ReceivedCalls().Any()).IsFalse();
            await Assert.That(f.egress.ReceivedCalls().Any()).IsFalse();
        });
    }

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Changing_configuration_does_not_change_a_prepared_round(bool streaming, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, streaming);
            await f.PrepareAsync(ct);
            ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).PlatformStreamingEnabled = !streaming;
            await f.Db.SaveChangesAsync(ct);
            f.media.CheckAsync(ct).Returns(new LiveSoloMediaReadiness(false, false, false));
            var store = f.Store(f.Db);
            var left = await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Left.Id, f.Match.ConcurrencyStamp, f.Now, ct);
            await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Right.Id, left.Match!.ConcurrencyStamp, f.Now, ct);
            var result = await store.StartCountdownAsync(new(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, f.Round.ConcurrencyStamp, f.Now), ct);
            await Assert.That(result.Failure).IsEqualTo(streaming ? LiveSoloFailure.MediaUnavailable : null);
            await Assert.That((await store.FindRoundAsync(f.Competition.Id, f.Match.Id, f.Round.Id, f.Left.Id, false, f.Now, ct))!.PlatformStreamingEnabled).IsEqualTo(streaming);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Disabled_round_rejects_room_preparation_without_contacting_media(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            await f.PrepareAsync(ct);
            var media = new LiveSoloMediaStore(f.Db, new CompetitionModerationAuthorizer(f.Db), Substitute.For<IMfaAuthenticationStore>(),
                f.media, Substitute.For<IPostCommitMessagePublisher>(), f.Clock);
            var denied = await media.PrepareAsync(new(f.Competition.Id, f.Match.Id, f.Owner.Id, f.Match.ConcurrencyStamp), ct);
            await Assert.That(denied.Failure).IsEqualTo(LiveSoloMediaFailure.Disabled);
            await Assert.That(await media.ReadAsync(f.Competition.Id, f.Match.Id, f.Left.Id, ct)).IsNull();
            await Assert.That(f.media.ReceivedCalls().Any()).IsFalse();
            await Assert.That(await f.Db.LiveSoloMediaSessions.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Next_non_streaming_round_retires_an_existing_room_without_a_screen_alert(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await f.PrepareAsync(ct);
            var store = f.Store(f.Db);
            var voided = await store.ApplyAsync(new AdjudicateLiveSoloMatch(f.Competition.Id, f.Match.Id, f.Owner.Id,
                f.Match.ConcurrencyStamp, f.Round.Id, f.Round.ConcurrencyStamp, f.Round.TimelineRevision,
                LiveSoloJudgeAction.VoidRound, null, "Synthetic next round"), ct);
            await Assert.That(voided.Failure).IsNull();
            ((LiveSoloCompetitionModeConfiguration)f.Competition.ModeConfiguration!).PlatformStreamingEnabled = false;
            await f.Db.SaveChangesAsync(ct);
            var group = await store.SaveGroupAsync(f.Competition.Id, f.Owner.Id, new(null, "Next group", true, null,
                f.Entries.Select(x => new LiveSoloQuestionGroupEntry(x.Id, null)).ToArray(), null), f.Now, ct);
            var prepared = await store.PrepareRoundAsync(new(f.Competition.Id, f.Match.Id, f.Owner.Id, voided.Match!.ConcurrencyStamp,
                group.Group!.Id, f.Now), ct);
            await Assert.That(prepared.Failure).IsNull();
            await Assert.That(prepared.Round!.PlatformStreamingEnabled).IsFalse();
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            await Assert.That(session.State).IsEqualTo(LiveSoloMediaState.Stopping);
            await Assert.That((await f.Db.LiveSoloMatches.SingleAsync(ct)).CurrentMediaSessionId).IsNull();
            var media = new LiveSoloMediaStore(f.Db, new CompetitionModerationAuthorizer(f.Db), Substitute.For<IMfaAuthenticationStore>(),
                f.media, Substitute.For<IPostCommitMessagePublisher>(), f.Clock);
            await media.RefreshAsync(new(session.Id, session.RoomIdentity), ct);
            await Assert.That(session.State).IsEqualTo(LiveSoloMediaState.Stopped);
            await Assert.That(await f.Db.Notifications.CountAsync(ct)).IsEqualTo(0);
            await f.media.Received(1).StopRoomAsync(session.RoomIdentity, ct);
            await f.media.DidNotReceive().CheckAsync(ct);
            await f.media.DidNotReceive().ObserveAsync(Arg.Any<string>(), ct);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Migration_preserves_existing_streaming_and_new_competitions_default_to_optional(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var owner = new User { Id = Guid.NewGuid(), UserName = "streaming-owner", NormalizedUserName = "STREAMING-OWNER", Email = "owner@example.test", PasswordHash = "unused", CreatedAt = now };
            var competition = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = owner.Id, Title = "Optional streaming", StartAt = now, EndAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now, FlagDerivationSecret = new byte[32] };
            competition.ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.LiveSolo, competition.Id);
            var group = new LiveSoloQuestionGroup { Id = Guid.NewGuid(), CompetitionId = competition.Id, Name = "Group" };
            var match = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = competition.Id, CreatedAt = now };
            var round = new LiveSoloRound { Id = Guid.NewGuid(), MatchId = match.Id, QuestionGroupId = group.Id, CreatedAt = now, PlatformStreamingEnabled = false };
            db.AddRange(owner, competition, group, match, round); await db.SaveChangesAsync(ct);
            await db.GetService<IMigrator>().MigrateAsync("20261010065909_CompetitionChallengeTiming", ct);
            db.ChangeTracker.Clear();
            await db.Database.MigrateAsync(ct);
            await Assert.That((await db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).PlatformStreamingEnabled).IsTrue();
            await Assert.That((await db.LiveSoloRounds.SingleAsync(ct)).PlatformStreamingEnabled).IsTrue();
            await Assert.That(((LiveSoloCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.LiveSolo, Guid.NewGuid())).PlatformStreamingEnabled).IsFalse();
        });
    }
}

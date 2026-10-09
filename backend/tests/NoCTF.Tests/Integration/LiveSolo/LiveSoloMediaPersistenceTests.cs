using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloMediaPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Private_generations_use_opaque_identities_and_sharing_state_comes_from_the_gateway_not_browser_claims(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var auth = Authentication(); var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), auth, fixture.media, fixture.Clock);
            var prepared = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            await Assert.That(prepared.Failure).IsNull();
            var session = await fixture.Db.LiveSoloMediaSessions.Include(x => x.Participants).SingleAsync(x => x.Id == prepared.Session!.Id, ct);
            await Assert.That(Guid.TryParse(session.RoomIdentity, out _)).IsTrue();
            foreach (var member in session.Participants) await Assert.That(Guid.TryParse(member.Identity, out _)).IsTrue();
            fixture.media.AuthorizeAsync(Arg.Any<LiveSoloMediaAuthorization>(), ct).Returns(new LiveSoloMediaToken("ws://localhost", "test-token", fixture.Now.AddMinutes(1)));
            var join = new JoinLiveSoloMedia(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, session.Generation,
                LiveSoloMediaRole.Publisher, fixture.Left.TokenVersion, new(AuthenticationMethod.Password, fixture.Now));
            await Assert.That((await store.JoinAsync(join, ct)).Failure).IsNull();
            await fixture.media.Received(1).AuthorizeAsync(Arg.Is<LiveSoloMediaAuthorization>(x => x != null && x.Role == LiveSoloMediaRole.Publisher
                && !x.MaySubscribe && x.ParticipantIdentity == session.Participants.Single(m => m.UserId == fixture.Left.Id).Identity), ct);
            await Assert.That((await store.JoinAsync(join with { Role = LiveSoloMediaRole.Judge }, ct)).Failure).IsEqualTo(LiveSoloMediaFailure.Unauthorized);
            fixture.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation(session.Participants.Select(x =>
                new LiveSoloObservedScreen(x.Identity, LiveSoloScreenState.Sharing, "screen-" + x.Identity, fixture.Now)).ToArray()));
            await store.RefreshAsync(new(session.Id, session.RoomIdentity), ct);
            await Assert.That(session.Participants.All(x => x.ScreenState == LiveSoloScreenState.Sharing)).IsTrue();
            fixture.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([]));
            await store.RefreshAsync(new(session.Id, session.RoomIdentity), ct);
            await Assert.That(session.Participants.All(x => x.ScreenState == LiveSoloScreenState.Disconnected)).IsTrue();
            await Assert.That((await fixture.Db.LiveSoloMatches.SingleAsync(ct)).State).IsEqualTo(LiveSoloMatchState.Preparing);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Lost_authentication_retires_the_room_and_old_generation_cannot_obtain_another_token(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var auth = Authentication(); var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), auth, fixture.media, fixture.Clock);
            var first = await store.PrepareAsync(await Prepare(fixture, ct), ct); var generation = first.Session!.Generation;
            fixture.media.AuthorizeAsync(Arg.Any<LiveSoloMediaAuthorization>(), ct).Returns(new LiveSoloMediaToken("ws://localhost", "test-token", fixture.Now.AddMinutes(1)));
            var join = new JoinLiveSoloMedia(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, generation, LiveSoloMediaRole.Publisher,
                fixture.Left.TokenVersion, new(AuthenticationMethod.Password, fixture.Now));
            await Assert.That((await store.JoinAsync(join, ct)).Failure).IsNull();
            auth.ValidateContextsAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), ct).Returns(call =>
                call.ArgAt<IReadOnlyList<MfaContextValidationRequest>>(0).ToDictionary(x => x.Key, _ => (MfaFailure?)MfaFailure.AccountUnavailable));
            await store.RefreshAsync(new(first.Session.Id, (await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == first.Session.Id, ct)).RoomIdentity), ct);
            await Assert.That((await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == first.Session.Id, ct)).State).IsEqualTo(LiveSoloMediaState.Stopped);
            await Assert.That((await fixture.Db.LiveSoloMediaGrants.SingleAsync(ct)).RevokedAt).IsNotNull();
            await fixture.media.Received(1).StopRoomAsync(Arg.Any<string>(), ct);
            await Assert.That((await store.JoinAsync(join, ct)).Failure).IsEqualTo(LiveSoloMediaFailure.InvalidGeneration);
            var replacement = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            await Assert.That(replacement.Failure).IsNull();
            await Assert.That(replacement.Session!.Generation).IsNotEqualTo(generation);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Preparing_state_recovers_after_room_creation_fails_without_claiming_readiness(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct); var auth = Authentication();
            fixture.media.CreateRoomAsync(Arg.Any<string>(), ct).Returns(Task.FromException(new HttpRequestException("Unavailable")));
            var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), auth, fixture.media, fixture.Clock);
            await Assert.That(async () => await store.PrepareAsync(await Prepare(fixture, ct), ct)).Throws<HttpRequestException>();
            var id = (await fixture.Db.LiveSoloMatches.SingleAsync(ct)).CurrentMediaSessionId!.Value;
            await Assert.That((await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == id, ct)).State).IsEqualTo(LiveSoloMediaState.Preparing);
            fixture.media.CreateRoomAsync(Arg.Any<string>(), ct).Returns(Task.CompletedTask);
            await store.RefreshAsync(new(id, (await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == id, ct)).RoomIdentity), ct);
            await Assert.That((await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == id, ct)).State).IsEqualTo(LiveSoloMediaState.Ready);
        });
    }
    [Test, Timeout(300_000)]
    public async Task A_missing_room_is_retired_and_replacement_has_a_new_identity_and_generation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), Authentication(), fixture.media, fixture.Clock);
            var first = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            var session = await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == first.Session!.Id, ct);
            fixture.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([], false));
            await store.RefreshAsync(new(session.Id, session.RoomIdentity), ct);
            await Assert.That(session.State).IsEqualTo(LiveSoloMediaState.Stopped);
            await Assert.That((await fixture.Db.LiveSoloMatches.SingleAsync(ct)).CurrentMediaSessionId).IsNull();
            var next = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            await Assert.That(next.Session!.Generation).IsNotEqualTo(first.Session!.Generation);
            await Assert.That((await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == next.Session.Id, ct)).RoomIdentity).IsNotEqualTo(session.RoomIdentity);
            await fixture.media.Received(1).StopRoomAsync(session.RoomIdentity, ct);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Disabling_the_mode_or_banning_a_roster_team_denies_tokens_before_the_periodic_review(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), Authentication(), fixture.media, fixture.Clock);
            var first = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            var join = new JoinLiveSoloMedia(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, first.Session!.Generation,
                LiveSoloMediaRole.Publisher, fixture.Left.TokenVersion, new(AuthenticationMethod.Password, fixture.Now));
            var config = await fixture.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct);
            config.Enabled = false; await fixture.Db.SaveChangesAsync(ct);
            await Assert.That((await store.JoinAsync(join, ct)).Failure).IsEqualTo(LiveSoloMediaFailure.Unauthorized);
            config.Enabled = true;
            (await fixture.Db.Teams.SingleAsync(x => x.Members.Any(m => m.UserId == fixture.Left.Id), ct)).IsBanned = true;
            await fixture.Db.SaveChangesAsync(ct);
            await Assert.That((await store.JoinAsync(join, ct)).Failure).IsEqualTo(LiveSoloMediaFailure.Unauthorized);
            await fixture.media.DidNotReceive().AuthorizeAsync(Arg.Any<LiveSoloMediaAuthorization>(), ct);
            await store.RefreshAsync(new(first.Session.Id, (await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == first.Session.Id, ct)).RoomIdentity), ct);
            await Assert.That((await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == first.Session.Id, ct)).State).IsEqualTo(LiveSoloMediaState.Stopped);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Stale_prepare_revision_does_not_create_a_second_room_and_unknown_participants_are_removed(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), Authentication(), fixture.media, fixture.Clock);
            var command = await Prepare(fixture, ct); var prepared = await store.PrepareAsync(command, ct);
            await Assert.That((await store.PrepareAsync(command, ct)).Failure).IsEqualTo(LiveSoloMediaFailure.InvalidGeneration);
            var session = await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == prepared.Session!.Id, ct);
            fixture.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([
                new("unknown", LiveSoloScreenState.Sharing, "not-a-roster-track", fixture.Now)]));
            await store.RefreshAsync(new(session.Id, session.RoomIdentity), ct);
            await fixture.media.Received(1).DisconnectAsync(session.RoomIdentity, "unknown", ct);
            await fixture.media.Received(1).CreateRoomAsync(session.RoomIdentity, ct);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Database_outage_retires_the_scheduler_bound_room_even_when_the_session_can_no_longer_be_read(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await ClearFixtureRoom(fixture, ct);
            var store = new LiveSoloMediaStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), Authentication(), fixture.media, fixture.Clock);
            var prepared = await store.PrepareAsync(await Prepare(fixture, ct), ct);
            var session = await fixture.Db.LiveSoloMediaSessions.SingleAsync(x => x.Id == prepared.Session!.Id, ct);
            var command = new RefreshLiveSoloMedia(session.Id, session.RoomIdentity);
            fixture.media.ClearReceivedCalls();
            await fixture.StopDatabaseAsync(ct);
            Exception? failure = null;
            try { await store.RefreshAsync(command, ct); } catch (Exception exception) { failure = exception; }
            await Assert.That(failure).IsNotNull();
            await fixture.media.Received(1).StopRoomAsync(session.RoomIdentity, Arg.Any<CancellationToken>());
            await fixture.media.DidNotReceive().CreateRoomAsync(session.RoomIdentity, Arg.Any<CancellationToken>());
        });
    }
    private static IMfaAuthenticationStore Authentication()
    {
        var auth = Substitute.For<IMfaAuthenticationStore>();
        auth.ValidateContextsAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.ArgAt<IReadOnlyList<MfaContextValidationRequest>>(0).ToDictionary(x => x.Key, _ => (MfaFailure?)null));
        return auth;
    }
    private static async Task ClearFixtureRoom(LiveSoloMatchPersistenceTests.Fixture fixture, CancellationToken ct)
    {
        (await fixture.Db.LiveSoloMatches.SingleAsync(ct)).CurrentMediaSessionId = null;
        foreach (var session in await fixture.Db.LiveSoloMediaSessions.ToArrayAsync(ct)) session.State = LiveSoloMediaState.Stopped;
        await fixture.Db.SaveChangesAsync(ct);
    }
    private static async Task<PrepareLiveSoloMedia> Prepare(LiveSoloMatchPersistenceTests.Fixture fixture, CancellationToken ct) =>
        new(fixture.Competition.Id, fixture.Match.Id, fixture.Owner.Id,
            (await fixture.Db.LiveSoloMatches.AsNoTracking().SingleAsync(x => x.Id == fixture.Match.Id, ct)).ConcurrencyStamp);
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloMediaAlertPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Screen_loss_creates_one_staff_only_alert_per_interruption_without_changing_results(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var messages = Substitute.For<IPostCommitMessagePublisher>(); var store = Store(f, f.Db, messages);
            var session = await f.Db.LiveSoloMediaSessions.Include(x => x.Participants).SingleAsync(ct);
            var command = new RefreshLiveSoloMedia(session.Id, session.RoomIdentity);
            await store.RefreshAsync(command, ct);
            var right = session.Participants.Single(x => x.UserId == f.Right.Id);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([new(right.Identity, LiveSoloScreenState.Sharing, "right", f.Now)]));
            await store.RefreshAsync(command, ct); await store.RefreshAsync(command, ct);
            var alert = await f.Db.Notifications.SingleAsync(x => x.Kind == NotificationKind.LiveSoloMediaInterrupted, ct);
            await Assert.That(alert.LiveSoloMediaAlertKind).IsEqualTo(LiveSoloMediaAlertKind.ScreenInterrupted);
            await Assert.That(alert.UserId).IsEqualTo(f.Left.Id); await Assert.That(alert.LiveSoloMatchId).IsEqualTo(f.Match.Id);
            await Assert.That(alert.TargetType).IsEqualTo(NotificationTargetType.CompetitionCollaborators);
            var reader = new NotificationReader(f.Db);
            await Assert.That(await reader.ListCompetitionAsync(f.Left.Id, f.Competition.Id, null, null, 100, ct)).IsEmpty();
            var staff = await reader.ListCompetitionAsync(f.Owner.Id, f.Competition.Id, null, null, 100, ct);
            await Assert.That(staff.Single().Content is LiveSoloMediaInterruptedNotificationContent).IsTrue();
            await messages.Received(1).PublishAsync(Arg.Is<LiveSoloMediaAlertCreated>(x => x != null && x.NotificationId == alert.Id));
            var match = await f.Db.LiveSoloMatches.AsNoTracking().SingleAsync(ct);
            await Assert.That(match.State).IsEqualTo(LiveSoloMatchState.Preparing); await Assert.That(match.LeftWins + match.RightWins).IsEqualTo(0);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation(session.Participants.Select(p =>
                new LiveSoloObservedScreen(p.Identity, LiveSoloScreenState.Sharing, "track", f.Now)).ToArray()));
            await store.RefreshAsync(command, ct); f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([]));
            await store.RefreshAsync(command, ct);
            await Assert.That(await f.Db.Notifications.CountAsync(x => x.Kind == NotificationKind.LiveSoloMediaInterrupted, ct)).IsEqualTo(3);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Room_loss_or_revoked_roster_creates_a_distinct_room_alert_and_repeated_retirement_is_idempotent(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            foreach (var reason in new[] { LiveSoloMediaAlertKind.RoomUnavailable, LiveSoloMediaAlertKind.AuthorizationChanged })
            {
                await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
                var store = Store(f, f.Db, Substitute.For<IPostCommitMessagePublisher>());
                var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct); var command = new RefreshLiveSoloMedia(session.Id, session.RoomIdentity);
                await store.RefreshAsync(command, ct);
                if (reason == LiveSoloMediaAlertKind.RoomUnavailable) f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([], false));
                else { (await f.Db.Teams.SingleAsync(x => x.Id == f.LeftTeam.Id, ct)).IsBanned = true; await f.Db.SaveChangesAsync(ct); }
                await store.RefreshAsync(command, ct); await store.RefreshAsync(command, ct);
                var alert = await f.Db.Notifications.SingleAsync(x => x.Kind == NotificationKind.LiveSoloMediaInterrupted, ct);
                await Assert.That(alert.LiveSoloMediaAlertKind).IsEqualTo(reason); await Assert.That(alert.UserId).IsNull();
                await Assert.That((await f.Db.LiveSoloMediaSessions.SingleAsync(ct)).State).IsEqualTo(LiveSoloMediaState.Stopped);
                await Assert.That((await f.Db.LiveSoloMatches.AsNoTracking().SingleAsync(ct)).State).IsEqualTo(LiveSoloMatchState.Preparing);
            }
        });
    }
    [Test, Timeout(300_000)]
    public async Task A_failed_transition_commit_rolls_back_the_alert_and_discards_uncommitted_delivery(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var store = Store(f, f.Db, Substitute.For<IPostCommitMessagePublisher>()); var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
            var command = new RefreshLiveSoloMedia(session.Id, session.RoomIdentity); await store.RefreshAsync(command, ct);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([]));
            await using var failing = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(f.Options).AddInterceptors(new RejectCommit()).Options);
            var messages = Substitute.For<IPostCommitMessagePublisher>(); store = Store(f, failing, messages);
            await Assert.That(async () => await store.RefreshAsync(command, ct)).Throws<InvalidOperationException>();
            await Assert.That(await f.Db.Notifications.CountAsync(x => x.Kind == NotificationKind.LiveSoloMediaInterrupted, ct)).IsEqualTo(0);
            messages.Received(1).DiscardPendingMessages(); await messages.DidNotReceive().FlushCommittedMessagesAsync();
        });
    }
    private static LiveSoloMediaStore Store(LiveSoloMatchPersistenceTests.Fixture f, NoCtfDbContext db, IPostCommitMessagePublisher messages)
    {
        var authentication = Substitute.For<IMfaAuthenticationStore>();
        authentication.ValidateContextsAsync(Arg.Any<IReadOnlyList<MfaContextValidationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<string, MfaFailure?>)call.Arg<IReadOnlyList<MfaContextValidationRequest>>()!.ToDictionary(x => x.Key, _ => (MfaFailure?)null));
        return new(db, new CompetitionModerationAuthorizer(db), authentication, f.media, messages, f.Clock);
    }
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected rollback");
    }
}

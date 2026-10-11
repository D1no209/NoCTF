using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloEventApplicabilityTests
{
    [Test, Timeout(300_000)]
    public async Task Legacy_ordinary_events_remain_in_staff_audit_but_are_removed_from_participant_history_and_public_delivery(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Competition.AccessMode = CompetitionAccessMode.Public;
            var events = new CompetitionEventStore(f.Db, Substitute.For<IPostCommitMessagePublisher>());
            Guid legacy;
            await using (var tx = await f.Db.Database.BeginTransactionAsync(ct))
            {
                legacy = await events.RecordAsync(new(f.Competition.Id, CompetitionEventKind.ChallengePublished,
                    CompetitionEventLevel.Information, CompetitionEventVisibility.Public, f.Now, CompetitionChallengeId: f.Entries[0].Id), ct);
                await events.RecordAsync(new(f.Competition.Id, CompetitionEventKind.AnnouncementPublished,
                    CompetitionEventLevel.Information, CompetitionEventVisibility.Public, f.Now), ct);
                await f.Db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            var query = new CompetitionEventQuery(f.Competition.Id, f.Left.Id, null, null, null, null, null, null,
                f.Now.AddMinutes(-1), f.Now.AddMinutes(1), null, null, 50);
            var participant = await events.QueryAsync(query, ct);
            await Assert.That(participant.Items!.Any(x => x.Kind == CompetitionEventKind.ChallengePublished)).IsFalse();
            await Assert.That(participant.Items!.Any(x => x.Kind == CompetitionEventKind.AnnouncementPublished)).IsTrue();
            var staff = await events.QueryAsync(query with { UserId = f.Owner.Id }, ct);
            await Assert.That(staff.Items!.Any(x => x.Kind == CompetitionEventKind.ChallengePublished)).IsTrue();
            var applicability = new CompetitionEventApplicabilityReader(f.Db);
            await Assert.That(await applicability.CanPublishAsync(f.Competition.Id, CompetitionEventKind.ChallengePublished, ct)).IsFalse();
            await Assert.That(await applicability.HasOrdinaryScoreboardAsync(f.Competition.Id, ct)).IsFalse();
            await Assert.That(await applicability.CanPublishAsync(f.Competition.Id, CompetitionEventKind.AnnouncementPublished, ct)).IsTrue();
            await Assert.That(await f.Db.CompetitionWebhookOutboxEvents.AnyAsync(x => x.EventId == legacy, ct)).IsFalse();
            var target = Guid.NewGuid();
            f.Db.CompetitionWebhookDeliveries.Add(new() { CompetitionId = f.Competition.Id, EventId = legacy, TargetId = target,
                State = CompetitionWebhookDeliveryState.Pending, DomainEventCreatedAt = f.Now, OutboxPersistedAt = f.Now, CreatedAt = f.Now, NextRetryAt = f.Now });
            await f.Db.SaveChangesAsync(ct);
            var protector = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
            var deliveries = new CompetitionWebhookDeliveryStore(f.Db, protector,
                new GetChallenge(Substitute.For<IChallengeManagementStore>()), new GetCompetitionTracks(Substitute.For<ICompetitionTrackStore>()),
                Substitute.For<ILeaderboardCache>(), new(new Uri("https://example.test/"), 10, new HashSet<string>(), new HashSet<string>()),
                f.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<CompetitionWebhookDeliveryStore>.Instance);
            await Assert.That((await deliveries.PrepareBatchAsync(new(f.Competition.Id, legacy), 10, ct)).Deliveries.Count).IsEqualTo(0);
            f.Db.ChangeTracker.Clear();
            await Assert.That((await f.Db.CompetitionWebhookDeliveries.SingleAsync(ct)).State).IsEqualTo(CompetitionWebhookDeliveryState.Suppressed);
            await Assert.That(CompetitionEventApplicability.PubliclyApplicable(GameMode.Ctf, CompetitionEventKind.ChallengePublished)).IsTrue();
        });
    }
}

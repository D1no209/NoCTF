using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
[NotInParallel]
public sealed class PlatformAuditLogStoreTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Existing_immutable_audits_are_aggregated_without_a_new_log_table(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_platform_audits")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-05T00:00:00Z");
            var actorId = Guid.CreateVersion7(now);
            var targetId = Guid.CreateVersion7(now.AddMilliseconds(1));
            var competitionId = Guid.CreateVersion7(now.AddMilliseconds(2));
            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.AddRange(
                    CreateUser(actorId, "audit-actor", now),
                    CreateUser(targetId, "audit-target", now));
                seed.Competitions.Add(new CtfCompetition
                {
                    Id = competitionId,
                    Title = "Audit competition",
                    OwnerId = actorId,
                    Status = CompetitionStatus.Running,
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    FlagDerivationSecret = new byte[32],
                    StartAt = now.AddHours(-1),
                    EndAt = now.AddHours(1),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Notifications.Add(new UserAccountLifecycleChangedNotification
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(4)),
                    SourceType = NotificationSourceType.User,
                    SourceId = actorId,
                    TargetType = NotificationTargetType.PlatformAdministrators,
                    TargetId = Notification.PlatformAdministratorsTargetId,
                    UserId = targetId,
                    UserName = "audit-target",
                    UserLifecycleAction = UserAccountLifecycleAction.Disabled,
                    Reason = "policy",
                    Automatic = false,
                    RelatedType = EntityReferenceKind.User,
                    RelatedId = targetId,
                    SentAt = now.AddMinutes(2)
                });
                seed.Notifications.Add(new SsoExternalIdentityBindingChangedNotification
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(8)),
                    SourceType = NotificationSourceType.User,
                    SourceId = actorId,
                    TargetType = NotificationTargetType.PlatformAdministrators,
                    TargetId = Notification.PlatformAdministratorsTargetId,
                    UserId = targetId,
                    ActionValue = (int)SsoBindingAuditAction.AdministrativelyUnbound,
                    SentAt = now.AddSeconds(30)
                });
                seed.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(5)),
                    CompetitionId = competitionId,
                    Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Public,
                    ActorUserId = actorId,
                    SubjectType = EntityReferenceKind.Competition,
                    SubjectId = competitionId,
                    CompetitionStatus = CompetitionStatus.Running,
                    Reason = "started from immutable event",
                    OccurredAt = now.AddMinutes(1)
                });
                seed.CompetitionEvents.Add(new RuntimeStateChangedEvent
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(6)),
                    CompetitionId = competitionId,
                    Level = CompetitionEventLevel.Warning,
                    Visibility = CompetitionEventVisibility.Staff,
                    ActorUserId = actorId,
                    SubjectType = EntityReferenceKind.Competition,
                    SubjectId = competitionId,
                    Reason = "runtime state changed",
                    OccurredAt = now.AddMinutes(1.5)
                });
                seed.CompetitionEvents.Add(new CompetitionCreatedEvent
                {
                    Id = Guid.CreateVersion7(now.AddMilliseconds(7)),
                    CompetitionId = competitionId,
                    Level = CompetitionEventLevel.Information,
                    Visibility = CompetitionEventVisibility.Staff,
                    ActorUserId = actorId,
                    SubjectType = EntityReferenceKind.Competition,
                    SubjectId = competitionId,
                    CompetitionStatus = CompetitionStatus.Draft,
                    OccurredAt = now.AddMinutes(1.75)
                });
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using var db = new NoCtfDbContext(options);
            var store = new PlatformAuditLogStore(db);
            var all = await store.QueryAsync(
                new(null, null, null, null, actorId, null, null, 10),
                cancellationToken);
            await Assert.That(all).Count().IsEqualTo(4);
            await Assert.That(all[0].Kind).IsEqualTo(PlatformAuditKind.UserAccountLifecycle);
            await Assert.That(all[1].Kind).IsEqualTo(PlatformAuditKind.CompetitionEvent);
            await Assert.That(all[1].CompetitionEventKind)
                .IsEqualTo(CompetitionEventKind.CompetitionCreated);
            await Assert.That(all[2].Kind).IsEqualTo(PlatformAuditKind.CompetitionLifecycle);
            await Assert.That(all[2].CompetitionEventKind)
                .IsEqualTo(CompetitionEventKind.CompetitionLifecycleChanged);
            await Assert.That(all[2].CompetitionEventVisibility)
                .IsEqualTo(CompetitionEventVisibility.Public);
            await Assert.That(all[2].Reason).IsEqualTo("started from immutable event");
            await Assert.That(all[3].PlatformAdministrationAction)
                .IsEqualTo(PlatformAdministrationAction.SsoExternalIdentityUnbound);
            await Assert.That(all[3].ActorId).IsEqualTo(actorId);

            var competitionOnly = await store.QueryAsync(
                new(null, null, null, competitionId, null, null, null, 10),
                cancellationToken);
            await Assert.That(competitionOnly).Count().IsEqualTo(2);
            await Assert.That(competitionOnly[0].CompetitionId).IsEqualTo(competitionId);

            var eventOnly = await store.QueryAsync(
                new(
                    PlatformAuditKind.CompetitionEvent,
                    null,
                    null,
                    competitionId,
                    null,
                    null,
                    null,
                    10),
                cancellationToken);
            await Assert.That(eventOnly).Count().IsEqualTo(1);
            await Assert.That(eventOnly[0].CompetitionEventKind)
                .IsEqualTo(CompetitionEventKind.CompetitionCreated);
            await Assert.That(all.Any(item =>
                item.CompetitionEventKind == CompetitionEventKind.RuntimeStateChanged)).IsFalse();

            var firstPage = await store.QueryAsync(
                new(null, null, null, null, actorId, null, null, 1),
                cancellationToken);
            var secondPage = await store.QueryAsync(
                new(
                    null,
                    null,
                    null,
                    null,
                    actorId,
                    firstPage[0].OccurredAt,
                    firstPage[0].Id,
                    1),
                cancellationToken);
            await Assert.That(secondPage).Count().IsEqualTo(1);
            await Assert.That(secondPage[0].Id).IsEqualTo(all[1].Id);
        });
    }

    private static User CreateUser(Guid id, string userName, DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        };
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Management;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Find_filters_the_entity_before_projecting_the_competition_view(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_competition_management")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var laterCompetitionId = Guid.CreateVersion7();
            var draftCompetitionId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = ownerId,
                UserName = "administrator",
                NormalizedUserName = "ADMINISTRATOR",
                Email = "administrator@example.test",
                NormalizedEmail = "ADMINISTRATOR@EXAMPLE.TEST",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Competitions.AddRange(
                Competition(competitionId, ownerId, "Competition persistence", now,
                    CompetitionStatus.Published),
                Competition(laterCompetitionId, ownerId, "Later competition", now.AddHours(2),
                    CompetitionStatus.Published),
                Competition(draftCompetitionId, ownerId, "Hidden draft", now.AddHours(4),
                    CompetitionStatus.Draft));
            await db.SaveChangesAsync(cancellationToken);

            var store = new CompetitionManagementStore(db);
            var result = await store
                .FindAsync(competitionId, includeDraft: false, cancellationToken);
            var listed = await store.ListAsync(includeDraft: false, cancellationToken);

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Id).IsEqualTo(competitionId);
            await Assert.That(listed).Count().IsEqualTo(2);
            await Assert.That(listed[0].Id).IsEqualTo(laterCompetitionId);
            await Assert.That(listed[1].Id).IsEqualTo(competitionId);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Hard_delete_finished_competition_removes_scoped_data_but_preserves_global_records(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_finished_competition_delete")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var teamId = Guid.CreateVersion7();
            var runtimeId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = ownerId,
                UserName = "delete-administrator",
                NormalizedUserName = "DELETE-ADMINISTRATOR",
                Email = "delete-administrator@example.test",
                NormalizedEmail = "DELETE-ADMINISTRATOR@EXAMPLE.TEST",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Finished competition purge",
                Mode = GameMode.Ctf,
                StartAt = now.AddHours(-2),
                EndAt = now.AddHours(-1),
                Status = CompetitionStatus.Finished,
                ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32],
                CreatedAt = now.AddHours(-2),
                UpdatedAt = now,
                ConfigurationUpdatedAt = now,
                LifecycleAudits =
                [
                    new CompetitionLifecycleAudit
                    {
                        Id = Guid.CreateVersion7(),
                        CompetitionId = competitionId,
                        From = CompetitionStatus.Running,
                        To = CompetitionStatus.Finished,
                        ActorId = ownerId,
                        OccurredAt = now.AddHours(-1)
                    }
                ]
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Title = "Reusable template",
                Mode = GameMode.Ctf,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                UpdatedAt = now
            });
            db.Teams.Add(new Team
            {
                Id = teamId,
                CompetitionId = competitionId,
                Name = "Disposable registration",
                NormalizedName = "DISPOSABLE REGISTRATION",
                CaptainId = ownerId,
                MemberIds = [ownerId],
                InvitationToken = new string('d', 32),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = runtimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                Generation = 1,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerPool = "default",
                State = RuntimeState.Stopped,
                StoppedAt = now,
                CreatedAt = now.AddHours(-1),
                PublishedPorts =
                [
                    new RuntimePublishedPort
                    {
                        Id = Guid.CreateVersion7(),
                        RuntimeInstanceId = runtimeId,
                        CompetitionId = competitionId,
                        ContainerPort = 80,
                        HostPort = 61000,
                        AllocatedAt = now.AddHours(-1)
                    }
                ]
            });
            db.CompetitionEvents.Add(new CompetitionEvent
            {
                Id = Guid.CreateVersion7(),
                CompetitionId = competitionId,
                Kind = CompetitionEventKind.CompetitionLifecycleChanged,
                Level = CompetitionEventLevel.Information,
                Visibility = CompetitionEventVisibility.Public,
                ActorUserId = ownerId,
                TeamId = teamId,
                CompetitionChallengeId = competitionChallengeId,
                RuntimeInstanceId = runtimeId,
                CompetitionStatus = CompetitionStatus.Finished,
                OccurredAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();

            var deleted = await new AdminCompetitionStore(db).HardDeleteAsync(
                competitionId,
                ownerId,
                true,
                cancellationToken);

            db.ChangeTracker.Clear();
            await Assert.That(deleted).IsTrue();
            await Assert.That(await db.Competitions.IgnoreQueryFilters()
                .AnyAsync(item => item.Id == competitionId, cancellationToken)).IsFalse();
            await Assert.That(await db.Teams.IgnoreQueryFilters()
                .AnyAsync(item => item.CompetitionId == competitionId, cancellationToken)).IsFalse();
            await Assert.That(await db.CompetitionChallenges.IgnoreQueryFilters()
                .AnyAsync(item => item.CompetitionId == competitionId, cancellationToken)).IsFalse();
            await Assert.That(await db.RuntimeInstances
                .AnyAsync(item => item.CompetitionId == competitionId, cancellationToken)).IsFalse();
            await Assert.That(await db.CompetitionEvents
                .AnyAsync(item => item.CompetitionId == competitionId, cancellationToken)).IsFalse();
            await Assert.That(await db.Challenges.IgnoreQueryFilters()
                .AnyAsync(item => item.Id == challengeId, cancellationToken)).IsTrue();
            await Assert.That(await db.Users
                .AnyAsync(item => item.Id == ownerId, cancellationToken)).IsTrue();
        });
    }

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        string title,
        DateTimeOffset startAt,
        CompetitionStatus status) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            Mode = GameMode.Ctf,
            StartAt = startAt,
            EndAt = startAt.AddHours(1),
            Status = status,
            MaxTeamMembers = 5,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            CreatedAt = startAt,
            UpdatedAt = startAt,
            ConfigurationUpdatedAt = startAt
        };
}

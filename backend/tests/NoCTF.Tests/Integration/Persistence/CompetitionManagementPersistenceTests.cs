using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Application.Competitions.Management;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Create_competition_persists_the_current_configuration_schema_for_every_mode(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_defaults")
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

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
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
            await db.SaveChangesAsync(cancellationToken);

            var store = new CompetitionManagementStore(db);
            foreach (var descriptor in GameModeCatalog.All)
            {
                var result = await store.CreateAsync(new(
                    $"{descriptor.Mode} defaults",
                    null,
                    descriptor.Mode,
                    now.AddHours(1),
                    now.AddHours(2),
                    true,
                    5,
                    2,
                    ownerId,
                    now), cancellationToken);
                var competitionId = result.Competition!.Id;
                var persistedJson = await db.Competitions
                    .AsNoTracking()
                    .Where(competition => competition.Id == competitionId)
                    .Select(competition => competition.ConfigurationJson)
                    .SingleAsync(cancellationToken);
                using var document = System.Text.Json.JsonDocument.Parse(persistedJson);

                await Assert.That(document.RootElement
                        .GetProperty("schemaVersion")
                        .GetInt32())
                    .IsEqualTo(descriptor.CurrentSchemaVersion);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Find_filters_the_entity_before_projecting_the_competition_view(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
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
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels)
                .Services
                .BuildServiceProvider();
            var readModels = new CompetitionReadModelCache(
                cacheServices.GetRequiredService<IFusionCacheProvider>());

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
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

            var outbox = new OpenApiTransactionalMessageOutbox();
            var store = new CompetitionManagementStore(
                db,
                outbox,
                new CompetitionEventStore(db, outbox),
                readModels);
            var result = await store
                .FindAsync(competitionId, includeDraft: false, cancellationToken);
            var listed = await store.ListAsync(includeDraft: false, cancellationToken);

            var updated = await store.UpdateAsync(
                new UpdateCompetitionCommand(
                    competitionId,
                    "Renamed competition",
                    result!.Description,
                    result.StartTime,
                    result.EndTime,
                    result.TeamRegistrationAutoApprove,
                    result.MaxTeamMembers,
                    result.MaxConcurrentRuntimeInstancesPerTeam,
                    ownerId,
                    now.AddMinutes(1)),
                cancellationToken);
            var refreshed = await store
                .FindAsync(competitionId, includeDraft: false, cancellationToken);
            var refreshedList = await store.ListAsync(includeDraft: false, cancellationToken);

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Id).IsEqualTo(competitionId);
            await Assert.That(listed).Count().IsEqualTo(2);
            await Assert.That(listed[0].Id).IsEqualTo(laterCompetitionId);
            await Assert.That(listed[1].Id).IsEqualTo(competitionId);
            await Assert.That(updated).IsNotNull();
            await Assert.That(refreshed!.Title).IsEqualTo("Renamed competition");
            await Assert.That(refreshedList.Single(item => item.Id == competitionId).Title)
                .IsEqualTo("Renamed competition");
            var updateEvent = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(item => item.CompetitionId == competitionId
                    && item.Kind == CompetitionEventKind.CompetitionUpdated,
                    cancellationToken);
            await Assert.That(updateEvent.ActorUserId).IsEqualTo(ownerId);

            var runningCompetition = await db.Competitions.SingleAsync(
                item => item.Id == competitionId,
                cancellationToken);
            runningCompetition.Status = CompetitionStatus.Running;
            runningCompetition.AllowTeamRegistrationWhileRunning = true;
            await db.SaveChangesAsync(cancellationToken);

            var registrations = new TeamRegistrationStore(
                db,
                new OpenApiTransactionalMessageOutbox());
            var allowed = await registrations.TryCreateAsync(
                new(competitionId, ownerId, "Running team", now.AddMinutes(2), "default"),
                TeamRegistrationStatus.Approved,
                cancellationToken);
            await Assert.That(allowed.Team).IsNotNull();

            runningCompetition.AllowTeamRegistrationWhileRunning = false;
            await db.SaveChangesAsync(cancellationToken);
            var rejected = await registrations.TryCreateAsync(
                new(competitionId, Guid.NewGuid(), "Late team", now.AddMinutes(3), "default"),
                TeamRegistrationStatus.Approved,
                cancellationToken);
            await Assert.That(rejected.Failure)
                .IsEqualTo(TeamRegistrationFailure.RegistrationClosed);
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

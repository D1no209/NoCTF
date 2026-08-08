using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Application.Competitions.Management;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
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
            using var cacheServices = new ServiceCollection()
                .AddFusionCache(NoCtfCacheNames.ReadModels)
                .Services
                .BuildServiceProvider();
            var readModels = new CompetitionReadModelCache(
                cacheServices.GetRequiredService<IFusionCacheProvider>());

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

            var store = new CompetitionManagementStore(db, readModels: readModels);
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
                CompetitionStatus.Published,
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
                new(competitionId, ownerId, "Running team", now.AddMinutes(2)),
                TeamRegistrationStatus.Approved,
                cancellationToken);
            await Assert.That(allowed.Team).IsNotNull();

            runningCompetition.AllowTeamRegistrationWhileRunning = false;
            await db.SaveChangesAsync(cancellationToken);
            var rejected = await registrations.TryCreateAsync(
                new(competitionId, Guid.NewGuid(), "Late team", now.AddMinutes(3)),
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

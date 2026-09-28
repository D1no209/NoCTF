using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Application.Competitions.Management;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Access;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionManagementPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Create_competition_persists_the_matching_typed_configuration_for_every_mode(
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
                .ConfigureWarnings(warnings => warnings.Throw(
                    RelationalEventId.MultipleCollectionIncludeWarning))
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
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);

            var store = new CompetitionManagementStore(db);
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                var result = await store.CreateAsync(new(
                    $"{mode} defaults",
                    null,
                    mode,
                    now.AddHours(1),
                    now.AddHours(2),
                    true,
                    5,
                    2,
                    ownerId,
                    now), cancellationToken);
                var competitionId = result.Competition!.Id;
                var persisted = await db.Competitions
                    .AsNoTracking()
                    .AsSplitQuery()
                    .SingleAsync(competition => competition.Id == competitionId, cancellationToken);
                await Assert.That(persisted.ModeConfiguration!.Mode).IsEqualTo(mode);
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
                .ConfigureWarnings(warnings => warnings.Throw(
                    RelationalEventId.MultipleCollectionIncludeWarning))
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

            var outbox = new NoOpPostCommitMessagePublisher();
            var store = new CompetitionManagementStore(
                db,
                outbox,
                new CompetitionEventStore(db, outbox),
                readModels);
            var result = await store
                .FindAsync(competitionId, includeDraft: false, cancellationToken);
            var listed = await store.ListAsync(includeDraft: false, cancellationToken);
            var audience = new CompetitionAudienceReader(db, readModels);
            await Assert.That(await audience.GetAccessModeAsync(
                    competitionId,
                    cancellationToken))
                .IsEqualTo(CompetitionAccessMode.Public);

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
                    now.AddMinutes(1),
                    AccessMode: CompetitionAccessMode.StaffOnly,
                    WriteUpSubmissionRequired: true,
                    WriteUpSubmissionDeadlineHours: 72),
                cancellationToken);
            var refreshed = await store
                .FindAsync(competitionId, includeDraft: false, cancellationToken);
            var refreshedList = await store.ListAsync(includeDraft: false, cancellationToken);
            await Assert.That(await audience.GetAccessModeAsync(
                    competitionId,
                    cancellationToken))
                .IsEqualTo(CompetitionAccessMode.StaffOnly);

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Id).IsEqualTo(competitionId);
            await Assert.That(listed).Count().IsEqualTo(2);
            await Assert.That(listed[0].Id).IsEqualTo(laterCompetitionId);
            await Assert.That(listed[1].Id).IsEqualTo(competitionId);
            await Assert.That(updated).IsNotNull();
            await Assert.That(updated!.AccessMode)
                .IsEqualTo(CompetitionAccessMode.StaffOnly);
            await Assert.That(updated.WriteUpSubmissionRequired).IsTrue();
            await Assert.That(updated.WriteUpSubmissionDeadlineHours).IsEqualTo(72);
            await Assert.That(refreshed!.Title).IsEqualTo("Renamed competition");
            await Assert.That(refreshed.AccessMode)
                .IsEqualTo(CompetitionAccessMode.StaffOnly);
            await Assert.That(refreshed.WriteUpSubmissionRequired).IsTrue();
            await Assert.That(refreshed.WriteUpSubmissionDeadlineHours).IsEqualTo(72);
            await Assert.That(refreshedList.Single(item => item.Id == competitionId).Title)
                .IsEqualTo("Renamed competition");
            var updateEvent = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(item => item.CompetitionId == competitionId
                    && item.Kind == CompetitionEventKind.CompetitionUpdated,
                    cancellationToken);
            await Assert.That(updateEvent.ActorUserId).IsEqualTo(ownerId);
            var audienceEvent = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(item => item.CompetitionId == competitionId
                    && item.Kind == CompetitionEventKind.CompetitionAudienceChanged,
                    cancellationToken);
            await Assert.That(audienceEvent.CompetitionAccessMode)
                .IsEqualTo(CompetitionAccessMode.StaffOnly);
            await Assert.That(audienceEvent.PreviousCompetitionAccessMode)
                .IsEqualTo(CompetitionAccessMode.Public);
            await Assert.That(audienceEvent.CompetitionAudienceChangeKind)
                .IsEqualTo(CompetitionAudienceChangeKind.AccessMode);

            var runningCompetition = await db.Competitions.AsSplitQuery().SingleAsync(
                item => item.Id == competitionId,
                cancellationToken);
            runningCompetition.Status = CompetitionStatus.Running;
            runningCompetition.AllowTeamRegistrationWhileRunning = true;
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var runtimeId = Guid.CreateVersion7();
            db.Challenges.Add(new CtfChallenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Title = "Runtime access test",
                Definition = TestConfigurations.Definition(GameMode.Ctf),
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CtfCompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Order = 1,
                IsPublished = true,
                Rules = TestConfigurations.Rules(GameMode.Ctf),
                UpdatedAt = now
            });
            db.RuntimeInstances.Add(new PlayerRuntimeInstance
            {
                Id = runtimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                AccessMode = RuntimeAccessMode.Direct,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Running,
                CreatedAt = now,
                RunningAt = now,
                ExpiresAt = now.AddHours(1)
            });
            await db.SaveChangesAsync(cancellationToken);

            var accessUpdated = await store.UpdateAsync(new(
                competitionId,
                refreshed.Title,
                refreshed.Description,
                refreshed.StartTime,
                refreshed.EndTime,
                refreshed.TeamRegistrationAutoApprove,
                refreshed.MaxTeamMembers,
                refreshed.MaxConcurrentRuntimeInstancesPerTeam,
                ownerId,
                now.AddMinutes(2),
                true,
                refreshed.MaxActiveQuestionsPerTeam,
                refreshed.MaxParticipantMessagesBeforeHandlerReply,
                refreshed.AllowChallengeOwnersToHandleQuestions,
                refreshed.PracticeModeEnabled,
                refreshed.AccessMode,
                refreshed.WriteUpSubmissionRequired,
                refreshed.WriteUpSubmissionDeadlineHours,
                RuntimeAccessMode.WsrxOnly,
                TrafficCaptureEnabled: true,
                TrafficCaptureLimitBytes: 16 * 1_048_576),
                cancellationToken);

            await Assert.That(accessUpdated).IsNotNull();
            await Assert.That(accessUpdated!.RuntimeAccessMode)
                .IsEqualTo(RuntimeAccessMode.WsrxOnly);
            await Assert.That(accessUpdated.TrafficCaptureEnabled).IsTrue();
            await Assert.That(accessUpdated.TrafficCaptureLimitBytes)
                .IsEqualTo(16 * 1_048_576);
            var existingRuntime = await db.RuntimeInstances.AsNoTracking()
                .AsSplitQuery()
                .SingleAsync(runtime => runtime.Id == runtimeId, cancellationToken);
            await Assert.That(existingRuntime.AccessMode)
                .IsEqualTo(RuntimeAccessMode.Direct);
            await Assert.That(existingRuntime.TrafficCaptureEnabled).IsFalse();

            var registrations = new TeamRegistrationStore(
                db,
                new NoOpPostCommitMessagePublisher());
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
        new CtfCompetition
        {
            Id = id,
            OwnerId = ownerId,
            Title = title,
            StartAt = startAt,
            EndAt = startAt.AddHours(1),
            Status = status,
            MaxTeamMembers = 5,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            CreatedAt = startAt,
            UpdatedAt = startAt,
        };
}

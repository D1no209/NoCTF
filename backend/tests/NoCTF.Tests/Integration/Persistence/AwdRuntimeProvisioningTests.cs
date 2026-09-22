using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdRuntimeProvisioningTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Stopping_runtime_defers_only_its_team_then_quota_is_enforced_after_cleanup(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_runtime_quota")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            var stoppingTeamId = fixture.ActiveTeamIds.First();

            await using var db = new NoCtfDbContext(options);
            var competition = await db.Competitions.SingleAsync(
                item => item.Id == fixture.CompetitionId,
                cancellationToken);
            competition.MaxConcurrentRuntimeInstancesPerTeam = 1;
            var stoppingRuntime = new RuntimeInstance
            {
                Id = Guid.CreateVersion7(fixture.Now.AddSeconds(1)),
                CompetitionId = fixture.CompetitionId,
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = stoppingTeamId,
                Purpose = RuntimePurpose.Player,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Stopping,
                CreatedAt = fixture.Now
            };
            db.RuntimeInstances.Add(stoppingRuntime);
            await db.SaveChangesAsync(cancellationToken);

            var provisioner = new PostgresAwdRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(runnerPool: "awd-tests"),
                outbox,
                TimeProvider.System);
            var first = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(first).IsEqualTo(AwdRuntimeProvisioningOutcome.DeferredCleanup);
            await Assert.That(await db.RuntimeInstances.AsNoTracking().CountAsync(
                    runtime => runtime.CompetitionId == fixture.CompetitionId,
                    cancellationToken))
                .IsEqualTo(2);
            await Assert.That(await db.RuntimeInstances.AsNoTracking().CountAsync(
                    runtime => runtime.CompetitionChallengeId == fixture.CompetitionChallengeId
                        && runtime.TeamId == stoppingTeamId,
                    cancellationToken))
                .IsEqualTo(1);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).HasSingleItem();

            stoppingRuntime.State = RuntimeState.Stopped;
            stoppingRuntime.StoppedAt = fixture.Now.AddSeconds(2);
            await db.SaveChangesAsync(cancellationToken);
            var resumed = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(resumed).IsEqualTo(AwdRuntimeProvisioningOutcome.Applied);
            await Assert.That(await db.RuntimeInstances.AsNoTracking().CountAsync(
                    runtime => runtime.CompetitionId == fixture.CompetitionId,
                    cancellationToken))
                .IsEqualTo(3);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>().Count())
                .IsEqualTo(2);

            var secondTemplateId = Guid.CreateVersion7(fixture.Now.AddSeconds(2));
            var secondCompetitionChallengeId = Guid.CreateVersion7(fixture.Now.AddSeconds(2));
            var definitionJson = await db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Mode == GameMode.Awd)
                .Select(challenge => challenge.DefinitionJson)
                .SingleAsync(cancellationToken);
            db.Challenges.Add(new Challenge
            {
                Id = secondTemplateId,
                OwnerId = fixture.OwnerId,
                Mode = GameMode.Awd,
                Title = "Second AWD service",
                DefinitionJson = definitionJson,
                CreatedAt = fixture.Now,
                UpdatedAt = fixture.Now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = secondCompetitionChallengeId,
                CompetitionId = fixture.CompetitionId,
                ChallengeId = secondTemplateId,
                Order = 2,
                IsPublished = true,
                RulesJson = JsonSerializer.Serialize(
                    new AwdChallengeConfiguration(
                        AwdChallengeConfiguration.CurrentSchemaVersion),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                UpdatedAt = fixture.Now
            });
            await db.SaveChangesAsync(cancellationToken);
            var runtimeCount = await db.RuntimeInstances.AsNoTracking().CountAsync(
                runtime => runtime.CompetitionId == fixture.CompetitionId,
                cancellationToken);
            var dispatchCount = outbox.Published.OfType<DispatchRuntime>().Count();

            var rejected = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(rejected)
                .IsEqualTo(AwdRuntimeProvisioningOutcome.CapacityExceeded);
            await Assert.That(await db.RuntimeInstances.AsNoTracking().CountAsync(
                    runtime => runtime.CompetitionId == fixture.CompetitionId,
                    cancellationToken))
                .IsEqualTo(runtimeCount);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>().Count())
                .IsEqualTo(dispatchCount);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_receipts_wait_for_cleanup_and_stop_results_fence_replacements(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_runtime_cleanup")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var initialVersions = fixture.ActiveTeamIds
                .Select((teamId, index) => (teamId, Version: (long)(10 + index)))
                .ToDictionary(item => item.teamId, item => item.Version);

            await using var db = new NoCtfDbContext(options);
            foreach (var teamId in fixture.ActiveTeamIds)
            {
                db.RuntimeInstances.Add(new RuntimeInstance
                {
                    Id = Guid.CreateVersion7(fixture.Now.AddSeconds(initialVersions[teamId])),
                    CompetitionId = fixture.CompetitionId,
                    CompetitionChallengeId = fixture.CompetitionChallengeId,
                    TeamId = teamId,
                    Purpose = RuntimePurpose.Player,
                    RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = RuntimeProvider.Docker,
                    RunnerId = $"runner-{teamId:N}",
                    State = RuntimeState.Failed,
                    FailureCode = RuntimeFailureCode.ProviderUnavailable,
                    ProviderReceiptJson = JsonSerializer.Serialize(new { teamId }),
                    CreatedAt = fixture.Now
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            var firstOutbox = new RecordingOutbox();
            var provisioner = new PostgresAwdRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(runnerPool: "awd-tests"),
                firstOutbox,
                TimeProvider.System);

            var first = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(first).IsEqualTo(AwdRuntimeProvisioningOutcome.DeferredCleanup);
            db.ChangeTracker.Clear();
            var firstPass = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.TeamId)
                .ThenBy(runtime => runtime.CreatedAt)
                .ThenBy(runtime => runtime.Id)
                .ToListAsync(cancellationToken);
            await Assert.That(firstPass.Count).IsEqualTo(4);
            foreach (var teamId in fixture.ActiveTeamIds)
            {
                var old = firstPass.Single(runtime => runtime.TeamId == teamId
                    && runtime.ProviderReceiptJson != null);
                var replacement = firstPass.Single(runtime => runtime.TeamId == teamId
                    && runtime.ProviderReceiptJson == null);
                await Assert.That(old.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(old.FailureCode).IsNull();
                await Assert.That(replacement.State).IsEqualTo(RuntimeState.Queued);
                await Assert.That(replacement.Id).IsNotEqualTo(old.Id);
            }
            await Assert.That(firstOutbox.Published.OfType<StopRuntime>().Count()).IsEqualTo(2);
            await Assert.That(firstOutbox.Published.OfType<DispatchRuntime>().Count()).IsEqualTo(2);

            var acknowledgedTeamId = fixture.ActiveTeamIds[0];
            var failedTeamId = fixture.ActiveTeamIds[1];
            var acknowledgedOld = firstPass.Single(runtime => runtime.TeamId == acknowledgedTeamId
                && runtime.ProviderReceiptJson != null);
            var failedOld = firstPass.Single(runtime => runtime.TeamId == failedTeamId
                && runtime.ProviderReceiptJson != null);
            var acknowledgementOutbox = new RecordingOutbox();
            await RuntimeWriteBackHandler.Handle(
                new RuntimeStopped(
                    acknowledgedOld.Id,
                    acknowledgedOld.RunnerId!),
                db,
                acknowledgementOutbox,
                cancellationToken);
            await RuntimeWriteBackHandler.Handle(
                new RuntimeStopFailed(
                    failedOld.Id,
                    failedOld.RunnerId!,
                    RuntimeFailureCode.CleanupFailed),
                db,
                acknowledgementOutbox,
                cancellationToken);

            await Assert.That(acknowledgementOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            db.ChangeTracker.Clear();
            var acknowledgedStored = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                runtime => runtime.Id == acknowledgedOld.Id,
                cancellationToken);
            var failedStored = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                runtime => runtime.Id == failedOld.Id,
                cancellationToken);
            await Assert.That(acknowledgedStored.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(failedStored.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(failedStored.FailureCode)
                .IsEqualTo(RuntimeFailureCode.CleanupFailed);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Running_awd_competition_creates_and_dispatches_missing_team_runtimes_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();

            await using var db = new NoCtfDbContext(options);
            var provisioner = new PostgresAwdRuntimeProvisioner(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(runnerPool: "awd-tests"),
                outbox,
                TimeProvider.System);
            var first = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);
            var replay = await provisioner.EnsureAsync(
                fixture.CompetitionId,
                cancellationToken);

            await Assert.That(first).IsEqualTo(AwdRuntimeProvisioningOutcome.Applied);
            await Assert.That(replay).IsEqualTo(AwdRuntimeProvisioningOutcome.Idempotent);
            var runtimes = await db.RuntimeInstances.AsNoTracking()
                .OrderBy(runtime => runtime.TeamId)
                .ToListAsync(cancellationToken);
            await Assert.That(runtimes.Count).IsEqualTo(2);
            await Assert.That(runtimes.Select(runtime => runtime.TeamId!.Value).ToHashSet()
                .SetEquals(fixture.ActiveTeamIds)).IsTrue();
            await Assert.That(runtimes.All(runtime => runtime.State == RuntimeState.Queued
                && runtime.RuntimeProvider == RuntimeProvider.Docker
                && runtime.RuntimeKind == RuntimeKind.Container)).IsTrue();
            var dispatches = outbox.Published.OfType<DispatchRuntime>().ToArray();
            await Assert.That(dispatches.Length).IsEqualTo(2);
            await Assert.That(dispatches.Select(dispatch => dispatch.RuntimeInstanceId).ToHashSet()
                .SetEquals(runtimes.Select(runtime => runtime.Id))).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var blueUserId = Guid.CreateVersion7();
        var bannedUserId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var redTeamId = Guid.CreateVersion7();
        var blueTeamId = Guid.CreateVersion7();
        var bannedTeamId = Guid.CreateVersion7();
        db.Users.AddRange(
            User(ownerId, "awd-red", now),
            User(blueUserId, "awd-blue", now),
            User(bannedUserId, "awd-banned", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWD runtime provisioning",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            ConfigurationJson = JsonSerializer.Serialize(
                AwdConfiguration.Default,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Title = "AWD service",
            DefinitionJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(
                    AwdChallengeConfiguration.CurrentSchemaVersion,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition(
                            "awd-runtime:fixture",
                            Security: new(false, false, false, ["ALL"], [])),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                        FlagSource: RuntimeFlagSource.AwdRotation),
                    FlagInjection: new AwdFlagInjectionConfiguration(
                        "printf '%s' '${FLAG}' > /dev/shm/flag")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = JsonSerializer.Serialize(
                new AwdChallengeConfiguration(
                    AwdChallengeConfiguration.CurrentSchemaVersion),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            UpdatedAt = now
        });
        db.Teams.AddRange(
            Team(redTeamId, competitionId, ownerId, "Red", false, now),
            Team(blueTeamId, competitionId, blueUserId, "Blue", false, now),
            Team(bannedTeamId, competitionId, bannedUserId, "Banned", true, now));
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            ownerId,
            competitionId,
            competitionChallengeId,
            [redTeamId, blueTeamId]);
    }

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid captainId,
        string name,
        bool banned,
        DateTimeOffset now) => new()
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = captainId,
            MemberIds = [captainId],
            InvitationToken = id.ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            IsBanned = banned,
            RegisteredAt = now
        };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        IReadOnlyList<Guid> ActiveTeamIds);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}

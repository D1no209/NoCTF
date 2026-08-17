using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("AwdpPlayerRuntime")]
public sealed class AwdpAttackRuntimePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_start_is_stable_and_reset_rotates_generation_flag(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_attack_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            var starts = await Task.WhenAll(
                StartAsync(options, fixture, fixture.UserIds[0], cancellationToken),
                StartAsync(options, fixture, fixture.UserIds[0], cancellationToken));
            await Assert.That(starts.All(result => result.Failure is null)).IsTrue();
            await Assert.That(starts.Select(result => result.Runtime!.Id).Distinct().Count())
                .IsEqualTo(1);

            var secondTeam = await StartAsync(
                options,
                fixture,
                fixture.UserIds[1],
                cancellationToken);
            await Assert.That(secondTeam.Failure).IsNull();
            await Assert.That(secondTeam.Runtime!.Id).IsNotEqualTo(starts[0].Runtime!.Id);

            string firstFlag;
            await using (var verify = new NoCtfDbContext(options))
            {
                var runtimes = await verify.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.Purpose == RuntimePurpose.Player)
                    .OrderBy(runtime => runtime.TeamId)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(runtimes.Length).IsEqualTo(2);
                var flags = await verify.ChallengeFlags.AsNoTracking()
                    .Where(flag => flag.SpecificationKind == SpecificationKind.RuntimeGeneration)
                    .OrderBy(flag => flag.TeamId)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(flags.Length).IsEqualTo(2);
                await Assert.That(flags.Select(flag => flag.Flag).Distinct().Count()).IsEqualTo(2);
                await Assert.That(flags.All(flag => flag.ValidStart is null && flag.ValidUntil is null))
                    .IsTrue();
                firstFlag = flags.Single(flag => flag.TeamId == fixture.TeamIds[0]).Flag;
            }

            await AssertEnvironmentInjectionAsync(
                options,
                starts[0].Runtime!,
                firstFlag,
                cancellationToken);

            await MarkRunningAsync(options, starts[0].Runtime!, cancellationToken);
            var reset = await ResetAsync(options, fixture, fixture.UserIds[0], cancellationToken);
            await Assert.That(reset.Failure).IsNull();
            await Assert.That(reset.Runtime!.Generation).IsEqualTo(2);

            await using var final = new NoCtfDbContext(options);
            var firstGeneration = await final.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.SpecificationKind == SpecificationKind.RuntimeGeneration
                    && flag.SpecificationId == starts[0].Runtime!.Id,
                cancellationToken);
            var secondGeneration = await final.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.SpecificationKind == SpecificationKind.RuntimeGeneration
                    && flag.SpecificationId == reset.Runtime.Id,
                cancellationToken);
            await Assert.That(firstGeneration.ValidStart).IsNotNull();
            await Assert.That(firstGeneration.ValidUntil).IsNotNull();
            await Assert.That(secondGeneration.ValidStart).IsNull();
            await Assert.That(secondGeneration.ValidUntil).IsNull();
            await Assert.That(secondGeneration.Flag).IsNotEqualTo(firstGeneration.Flag);

            await AssertEnvironmentInjectionAsync(
                options,
                reset.Runtime!,
                secondGeneration.Flag,
                cancellationToken);
            await MarkRunningAsync(options, reset.Runtime!, cancellationToken);
            var stopped = await StopAsync(
                options,
                fixture,
                fixture.UserIds[0],
                cancellationToken);
            await Assert.That(stopped.Failure).IsNull();
            await using var stoppedVerification = new NoCtfDbContext(options);
            var invalidated = await stoppedVerification.ChallengeFlags.AsNoTracking()
                .SingleAsync(flag => flag.Id == secondGeneration.Id, cancellationToken);
            await Assert.That(invalidated.ValidUntil).IsNotNull();
        });
    }

    private static async Task<RuntimeMutationResult> StartAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await CreateStore(db).MutatePlayerRuntimeAsync(new(
            fixture.CompetitionId,
            fixture.CompetitionChallengeId,
            userId,
            RuntimeAction.Start,
            null,
            fixture.Now), cancellationToken);
    }

    private static async Task<RuntimeMutationResult> ResetAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await CreateStore(db).MutatePlayerRuntimeAsync(new(
            fixture.CompetitionId,
            fixture.CompetitionChallengeId,
            userId,
            RuntimeAction.Reset,
            null,
            fixture.Now.AddMinutes(1)), cancellationToken);
    }

    private static async Task<RuntimeMutationResult> StopAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await CreateStore(db).MutatePlayerRuntimeAsync(new(
            fixture.CompetitionId,
            fixture.CompetitionChallengeId,
            userId,
            RuntimeAction.Stop,
            null,
            fixture.Now.AddMinutes(2)), cancellationToken);
    }

    private static RuntimeInstanceStore CreateStore(NoCtfDbContext db) => new(
        db,
        new ChallengeRuntimeTemplateCatalog(),
        new FixedRuntimePlacementPolicy(RuntimeProvider.Docker, "awdp-tests"),
        new PostgresPerTeamRuntimeFlagStore(db),
        new NoopOutbox());

    private static async Task AssertEnvironmentInjectionAsync(
        DbContextOptions<NoCtfDbContext> options,
        RuntimeInstanceView runtime,
        string expectedFlag,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var entity = await db.RuntimeInstances.SingleAsync(
            item => item.Id == runtime.Id,
            cancellationToken);
        var challengeDefinition = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == runtime.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                challenge => challenge.Id,
                (_, challenge) => challenge.DefinitionJson)
            .SingleAsync(cancellationToken);
        var template = new ChallengeRuntimeTemplateCatalog().Get(
            GameMode.Awdp,
            challengeDefinition)!;
        var claim = (ClaimContainerRuntime)NoCTF.Worker.Runtime.RuntimeClaimFactory.Create(
            entity,
            GameMode.Awdp,
            template,
            challengeDefinition,
            expectedFlag);

        await Assert.That(entity.Purpose).IsEqualTo(RuntimePurpose.Player);
        await Assert.That(claim.Definition.Environment["FLAG"]).IsEqualTo(expectedFlag);
        await Assert.That(claim.Definition.PortMappings[31337]).IsEqualTo(0);
        await Assert.That(claim.Definition.UrlBindings!.Single().Exposure)
            .IsEqualTo(RuntimeExposure.OwnerOnly);
        entity.State = RuntimeState.Provisioning;
        entity.ProcessingVersion = 1;
        entity.RunnerId = "runner-1";
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task MarkRunningAsync(
        DbContextOptions<NoCtfDbContext> options,
        RuntimeInstanceView runtime,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await RuntimeWriteBackHandler.Handle(new RuntimeProvisioned(
            runtime.Id,
            1,
            runtime.Generation,
            "runner-1",
            RuntimeProvider.Docker,
            "{}",
            ["tcp://127.0.0.1:30000"],
            [0],
            DateTimeOffset.UtcNow.AddMinutes(15)),
            db,
            new NoopOutbox(),
            cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now);
        var challengeId = Guid.CreateVersion7(now);
        var competitionChallengeId = Guid.CreateVersion7(now);
        var userIds = new[] { Guid.CreateVersion7(now), Guid.CreateVersion7(now) };
        var teamIds = new[] { Guid.CreateVersion7(now), Guid.CreateVersion7(now) };
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Users.AddRange(userIds.Select((id, index) => new User
        {
            Id = id,
            UserName = $"player-{index}",
            NormalizedUserName = $"PLAYER-{index}",
            Email = $"player-{index}@example.test",
            NormalizedEmail = $"PLAYER-{index}@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        }));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP attack",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Status = CompetitionStatus.Running,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp),
            ConfigurationUpdatedAt = now,
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            MaxConcurrentRuntimeInstancesPerTeam = 2,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Title = "AWDP attack",
            DefinitionJson = JsonSerializer.Serialize(new AwdpChallengeConfiguration(
                AwdpChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                null,
                null,
                null,
                Runtime: new(
                    RuntimeAllocation.PerTeam,
                    new ContainerRuntimeDefinition(
                        "awdp-target:latest",
                        PortMappings: new Dictionary<int, int> { [31337] = 0 },
                        FlagEnvironmentVariableName: "FLAG",
                        InternalPorts: [31337]),
                    new RuntimeResourceLimits(64 * 1024 * 1024, 100_000_000, 64),
                    UrlBindings: [new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31337)],
                    FlagSource: RuntimeFlagSource.PerTeam)), json),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            BaseScore = 100,
            RulesJson = JsonSerializer.Serialize(new AwdpChallengeConfiguration(
                AwdpChallengeConfiguration.CurrentSchemaVersion,
                null,
                null,
                null,
                null,
                null,
                FlagTemplate: new PerTeamFlagTemplate("awdp", "[GUID]", false)), json),
            UpdatedAt = now
        });
        db.Teams.AddRange(teamIds.Select((id, index) => new Team
        {
            Id = id,
            CompetitionId = competitionId,
            Name = $"Team {index}",
            NormalizedName = $"TEAM {index}",
            CaptainId = userIds[index],
            MemberIds = [userIds[index]],
            InvitationToken = id.ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        }));
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, competitionChallengeId, userIds, teamIds);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        IReadOnlyList<Guid> UserIds,
        IReadOnlyList<Guid> TeamIds);

    private sealed class NoopOutbox : ITransactionalMessageOutbox
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}

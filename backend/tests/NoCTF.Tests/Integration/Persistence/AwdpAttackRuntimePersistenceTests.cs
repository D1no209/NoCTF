using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
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
    public async Task Successful_break_blocks_new_attack_runtimes_and_allows_read_only_flag_judgement(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_read_only_break")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            const string correctFlag = "flag{completed-awdp-break}";

            await using (var setup = new NoCtfDbContext(options))
            {
                setup.GameplayFacts.Add(new BreakAttemptGameplayFact
                {
                    Id = Guid.CreateVersion7(fixture.Now.AddSeconds(1)),
                    CompetitionId = fixture.CompetitionId,
                    CompetitionChallengeId = fixture.CompetitionChallengeId,
                    TeamId = fixture.TeamIds[0],
                    ActorUserId = fixture.UserIds[0],
                    Value = correctFlag,
                    ValueSha256 = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(correctFlag)),
                    OccurredAt = fixture.Now,
                    State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Correct,
                    UpdatedAt = fixture.Now
                });
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var db = new NoCtfDbContext(options);
            var judge = new JudgeAwdpBreakFlag(new AwdpBreakFlagJudge(db));
            var correct = await judge.ExecuteAsync(new(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserIds[0],
                correctFlag), cancellationToken);
            var wrong = await judge.ExecuteAsync(new(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserIds[0],
                "flag{wrong}"), cancellationToken);

            await Assert.That(correct.Judgement)
                .IsEqualTo(AwdpBreakFlagJudgement.Correct);
            await Assert.That(wrong.Judgement)
                .IsEqualTo(AwdpBreakFlagJudgement.Wrong);
            await Assert.That(await db.GameplayFacts.CountAsync(cancellationToken)).IsEqualTo(1);
            await Assert.That(await db.CompetitionEvents.CountAsync(cancellationToken)).IsEqualTo(0);
            await Assert.That(await db.Notifications.CountAsync(cancellationToken)).IsEqualTo(0);

            var restart = await CreateStore(db).MutatePlayerRuntimeAsync(new(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserIds[0],
                RuntimeAction.Start,
                null,
                fixture.Now.AddMinutes(1)), cancellationToken);
            await Assert.That(restart.Runtime).IsNull();
            await Assert.That(restart.Failure).IsEqualTo(RuntimeMutationFailure.InvalidState);
            await Assert.That(await db.RuntimeInstances.CountAsync(cancellationToken)).IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Attack_provisioning_accepts_worker_injected_flag(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_attack_plan")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var flag = "flag{awdp-runtime-env}";
            var message = await CreateAttackProvisioningMessageAsync(
                options,
                fixture,
                flag,
                storeFlag: true,
                includeInjectedFlag: true,
                cancellationToken);

            var reader = new AwdpAttackProvisioningPlanReader(
                new TestDbContextFactory(options));
            var plan = await reader.ReadAsync(message, cancellationToken);

            await Assert.That(plan.State).IsEqualTo(AwdpAttackProvisioningPlanState.Ready);
            await Assert.That(plan.Definition).IsNotNull();
            await Assert.That(plan.Definition!.Environment["FLAG"]).IsEqualTo(flag);
            await Assert.That(plan.Definition.Environment.ContainsKey("OLD_FLAG")).IsFalse();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Attack_provisioning_rejects_missing_runtime_flag_or_worker_injection(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_attack_plan_invalid")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var reader = new AwdpAttackProvisioningPlanReader(
                new TestDbContextFactory(options));

            var missingFlagRow = await CreateAttackProvisioningMessageAsync(
                options,
                fixture,
                "flag{missing-row}",
                storeFlag: false,
                includeInjectedFlag: true,
                cancellationToken);
            var missingFlagRowPlan = await reader.ReadAsync(
                missingFlagRow,
                cancellationToken);
            await Assert.That(missingFlagRowPlan.State)
                .IsEqualTo(AwdpAttackProvisioningPlanState.Invalid);
            await MarkRuntimeFailedAsync(
                options,
                missingFlagRow.RuntimeInstanceId,
                cancellationToken);

            var missingInjection = await CreateAttackProvisioningMessageAsync(
                options,
                fixture,
                "flag{missing-injection}",
                storeFlag: true,
                includeInjectedFlag: false,
                cancellationToken);
            var missingInjectionPlan = await reader.ReadAsync(
                missingInjection,
                cancellationToken);
            await Assert.That(missingInjectionPlan.State)
                .IsEqualTo(AwdpAttackProvisioningPlanState.Invalid);
        });
    }

    private static async Task MarkRuntimeFailedAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeInstanceId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtime = await db.RuntimeInstances.SingleAsync(
            item => item.Id == runtimeInstanceId,
            cancellationToken);
        runtime.State = RuntimeState.Failed;
        runtime.FailureCode = RuntimeFailureCode.InvalidConfiguration;
        await db.SaveChangesAsync(cancellationToken);
    }

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
                    .Where(runtime => runtime.Purpose == RuntimePurpose.AwdpAttack)
                    .OrderBy(runtime => runtime.TeamId)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(runtimes.Length).IsEqualTo(2);
                await Assert.That(runtimes.Select(runtime => runtime.TeamId).ToArray())
                    .IsEquivalentTo(fixture.TeamIds.Cast<Guid?>().ToArray());
                var adminRuntimes = await new AdminRuntimeStore(
                        verify,
                        new ChallengeRuntimeTemplateCatalog(),
                        new FixedRuntimePlacementPolicy(RuntimeProvider.Docker, "awdp-tests"),
                        new PerTeamRuntimeFlagStore(verify),
                        new NoopOutbox())
                    .ListAsync(new(
                            fixture.CompetitionId,
                            fixture.CompetitionChallengeId,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null),
                        null,
                        null,
                        10,
                        cancellationToken);
                var attackRuntimes = adminRuntimes
                    .Where(runtime => runtime.Purpose == RuntimePurpose.AwdpAttack)
                    .ToArray();
                await Assert.That(attackRuntimes.Length).IsEqualTo(2);
                await Assert.That(attackRuntimes.Select(runtime => runtime.SourceTeamId).ToArray())
                    .IsEquivalentTo(fixture.TeamIds.Cast<Guid?>().ToArray());
                await Assert.That(attackRuntimes.Select(runtime => runtime.SourceTeamName).ToArray())
                    .IsEquivalentTo(new string?[] { "Team 0", "Team 1" });
                var flags = await verify.ChallengeFlags.AsNoTracking()
                    .Where(flag => flag.SpecificationKind == SpecificationKind.RuntimeInstance)
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
            await Assert.That(reset.Runtime!.Id).IsNotEqualTo(starts[0].Runtime!.Id);

            await using var final = new NoCtfDbContext(options);
            var firstGeneration = await final.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.SpecificationKind == SpecificationKind.RuntimeInstance
                    && flag.SpecificationId == starts[0].Runtime!.Id,
                cancellationToken);
            var secondGeneration = await final.ChallengeFlags.AsNoTracking().SingleAsync(
                flag => flag.SpecificationKind == SpecificationKind.RuntimeInstance
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

    [Test]
    [Timeout(300_000)]
    public async Task Different_envelopes_apply_in_completion_order_without_generation_guards(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_completion_order")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var started = await StartAsync(
                options,
                fixture,
                fixture.UserIds[0],
                cancellationToken);
            await Assert.That(started.Runtime).IsNotNull();
            var runtime = started.Runtime!;
            var flag = await ReadRuntimeFlagAsync(options, runtime.Id, cancellationToken);
            await AssertEnvironmentInjectionAsync(
                options,
                runtime,
                flag.Flag,
                cancellationToken);

            await using var db = new NoCtfDbContext(options);
            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisionFailed(
                    runtime.Id,
                    RuntimeFailureCode.ProviderRejected,
                    "runner-1"),
                db,
                cancellationToken);
            await AssertRuntimeStateAsync(db, runtime.Id, RuntimeState.Failed, cancellationToken);

            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisioned(
                    runtime.Id,
                    "runner-1",
                    RuntimeProvider.Docker,
                    RuntimeReceiptTestData.ContainerData(runtime.Id),
                    [new RuntimeAccessEndpointMapping(0, "nc 127.0.0.1 30000", null, null)],
                    fixture.Now.AddMinutes(30),
                    [new RuntimePublishedPortMapping(null, 31337, 30000)]),
                db,
                new NoopOutbox(),
                cancellationToken);
            await AssertRuntimeStateAsync(db, runtime.Id, RuntimeState.Running, cancellationToken);
            var provisioned = await db.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == runtime.Id,
                cancellationToken);
            await Assert.That(provisioned.AccessEndpoints.Select(endpoint => endpoint.DirectAddress).OfType<string>())
                .IsEquivalentTo(["nc 127.0.0.1 30000"]);
            var reactivated = await db.ChallengeFlags.AsNoTracking().SingleAsync(
                item => item.Id == flag.Id,
                cancellationToken);
            await Assert.That(reactivated.ValidUntil).IsNull();

            await RuntimeWriteBackHandler.Handle(
                new RuntimeProvisionFailed(
                    runtime.Id,
                    RuntimeFailureCode.ProviderRejected,
                    "runner-1"),
                db,
                cancellationToken);
            await AssertRuntimeStateAsync(db, runtime.Id, RuntimeState.Failed, cancellationToken);
        });
    }

    private static async Task<ChallengeFlag> ReadRuntimeFlagAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await db.ChallengeFlags.AsNoTracking().SingleAsync(
            item => item.SpecificationKind == SpecificationKind.RuntimeInstance
                && item.SpecificationId == runtimeId,
            cancellationToken);
    }

    private static async Task AssertRuntimeStateAsync(
        NoCtfDbContext db,
        Guid runtimeId,
        RuntimeState expected,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var state = await db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => item.State)
            .SingleAsync(cancellationToken);
        await Assert.That(state).IsEqualTo(expected);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Admin_start_creates_a_team_bound_awdp_attack_runtime(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awdp_admin_attack_runtime")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);

            await using var db = new NoCtfDbContext(options);
            var store = new AdminRuntimeStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(RuntimeProvider.Docker, "awdp-tests"),
                new PerTeamRuntimeFlagStore(db),
                new NoopOutbox());
            var result = await store.MutateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.TeamIds[0],
                RuntimeAction.Start,
                null,
                fixture.Now,
                cancellationToken);

            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Runtime).IsNotNull();
            await Assert.That(result.Runtime!.Purpose).IsEqualTo(RuntimePurpose.AwdpAttack);
            await Assert.That(result.Runtime.TeamId).IsEqualTo(fixture.TeamIds[0]);

            var listed = await store.ListAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null),
                null,
                null,
                10,
                cancellationToken);
            await Assert.That(listed.Single().SourceTeamName).IsEqualTo("Team 0");
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
        new PerTeamRuntimeFlagStore(db),
        new NoopOutbox());

    private static async Task<ProvisionContainerRuntime> CreateAttackProvisioningMessageAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        string flag,
        bool storeFlag,
        bool includeInjectedFlag,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtimeId = Guid.CreateVersion7(fixture.Now.AddSeconds(1));
        var runtime = new AwdpAttackRuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamIds[0],
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerId = "runner-1",
            State = RuntimeState.Provisioning,
            CreatedAt = fixture.Now
        };
        db.RuntimeInstances.Add(runtime);
        if (storeFlag)
        {
            db.ChallengeFlags.Add(new RuntimeInstanceChallengeFlag
            {
                Id = Guid.CreateVersion7(fixture.Now.AddSeconds(2)),
                CompetitionChallengeId = fixture.CompetitionChallengeId,
                TeamId = fixture.TeamIds[0],
                SpecificationKind = SpecificationKind.RuntimeInstance,
                SpecificationId = runtimeId,
                Flag = flag,
                FlagSha256 = ManageChallengeFlags.Hash(flag),
                MatchKind = ChallengeFlagMatchKind.Exact,
                CreatedAt = fixture.Now
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        var challengeDefinition = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == fixture.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                challenge => challenge.Id,
                (_, challenge) => challenge)
            .SingleAsync(cancellationToken);
        var template = new ChallengeRuntimeTemplateCatalog().Get(
            challengeDefinition.Definition)!;
        var claim = (ProvisionContainerRuntime)NoCTF.Worker.Runtime.RuntimeClaimFactory.Create(
            runtime,
            "runner-1",
            GameMode.Awdp,
            template,
            challengeDefinition.Definition,
            flag);
        var environment = includeInjectedFlag
            ? claim.Definition.Environment
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return new(
            claim.RuntimeInstanceId,
            claim.RunnerId,
            claim.Definition with { Environment = environment });
    }

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
                (_, challenge) => challenge)
            .SingleAsync(cancellationToken);
        var template = new ChallengeRuntimeTemplateCatalog().Get(
            challengeDefinition.Definition)!;
        var claim = (ProvisionContainerRuntime)NoCTF.Worker.Runtime.RuntimeClaimFactory.Create(
            entity,
            "runner-1",
            GameMode.Awdp,
            template,
            challengeDefinition.Definition,
            expectedFlag);

        await Assert.That(entity.Purpose).IsEqualTo(RuntimePurpose.AwdpAttack);
        await Assert.That(entity.TeamId).IsEqualTo(runtime.TeamId);
        await Assert.That(claim.Definition.Environment["FLAG"]).IsEqualTo(expectedFlag);
        await Assert.That(claim.Definition.PortMappings[31337]).IsEqualTo(0);
        await Assert.That(claim.Definition.UrlBindings!.Single().Exposure)
            .IsEqualTo(RuntimeExposure.OwnerOnly);
        entity.State = RuntimeState.Provisioning;
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
            "runner-1",
            RuntimeProvider.Docker,
            RuntimeReceiptTestData.ContainerData(runtime.Id),
            [new RuntimeAccessEndpointMapping(0, "tcp://127.0.0.1:30000", null, null)],
            DateTimeOffset.UtcNow.AddMinutes(15),
            [new RuntimePublishedPortMapping(null, 31337, 30000)]),
            db,
            new NoopOutbox(),
            cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
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
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        }));
        db.Competitions.Add(new AwdpCompetition
        {
            Id = competitionId,
            Title = "AWDP attack",
            OwnerId = ownerId,
            Status = CompetitionStatus.Running,
            ModeConfiguration = TestConfigurations.Competition(GameMode.Awdp),
            StartAt = now.AddMinutes(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
            MaxConcurrentRuntimeInstancesPerTeam = 2,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new AwdpChallenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "AWDP attack",
            Definition = TestConfigurations.Definition(GameMode.Awdp, JsonSerializer.Serialize(new AwdpChallengeConfiguration(
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
                        Security: new(false, false, false, ["ALL"], []),
                        FlagEnvironmentVariableName: "FLAG",
                        InternalPorts: [31337]),
                    new RuntimeResourceLimits(64 * 1024 * 1024, 100_000_000, 64),
                    UrlBindings: [new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 31337)],
                    FlagSource: RuntimeFlagSource.PerTeam)), json)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new AwdpCompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Rules = TestConfigurations.Rules(
                GameMode.Awdp,
                """{"schemaVersion":4,"flagTemplate":{"header":"awdp","bodyTemplate":"[GUID]","leetLiteralText":false}}"""),
            UpdatedAt = now
        });
        db.Teams.AddRange(teamIds.Select((id, index) => new Team
        {
            Id = id,
            CompetitionId = competitionId,
            Name = $"Team {index}",
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

    private sealed class NoopOutbox : IPostCommitMessagePublisher
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}

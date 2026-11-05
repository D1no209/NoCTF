using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class CheckCapacityDockerTests
{
    public enum WaitingResult { Completed, Deadline, CallerCanceled }
    private const string PythonImage = "docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8";

    [Test]
    public async Task Awd_provider_execution_has_no_ambient_database_transaction()
    {
        await Assert.That(typeof(AwdCheckerHandler).GetCustomAttributes(typeof(Wolverine.Attributes.NonTransactionalAttribute), inherit: true)).HasSingleItem();
    }

    [Test, Timeout(300_000)]
    public async Task Awd_adapter_supplies_scoped_task_metadata_and_rejects_a_foreign_runtime_receipt(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var target = (await SeedAsync(fixture, 1, ct))[0];
        await using var callback = new ContainerBuilder(PythonImage).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
            .WithCommand("python", "-m", "http.server", "8080").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080)).Build();
        await callback.StartAsync(ct); await fixture.RefreshOwnerAsync(ct);
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(fixture.Capacity, fixture.RunnerOptions, fixture.Reconcilers, mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        using var clients = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var execute = new AwdCheckerExecutor(new(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions(), capacity)), clients.GetRequiredService<IHttpClientFactory>());
        var host = target.Receipt.Services.Single().InternalHost!; var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var work = new AwdCheckerWork(target.RuntimeId, target.FactId, RuntimeProvider.Docker, RuntimeKind.Container, target.Receipt, host,
            RuntimeExecutionFixture.Image, ["/bin/sh", "-c", "test \"$MODE\" = awd && test -n \"$NOCTF_AWD_TARGETS\" && test \"$NOCTF_TARGET_HOST\" = \"$EXPECTED_HOST\" && test \"$NOCTF_CALLBACK_TOKEN\" = capacity-test-credential && test \"$NOCTF_AWD_TASK_URL\" = http://capacity-callback:8080/task && test -z \"${NOCTF_AWD_FLAGS+x}\" && test \"$NOCTF_AWD_DEADLINE\" = \"$EXPECTED_DEADLINE\" && wget -qO- \"$NOCTF_CALLBACK_URL\" > /dev/null"],
            new Dictionary<string, string> { ["MODE"] = "awd", ["EXPECTED_HOST"] = host, ["EXPECTED_DEADLINE"] = deadline.ToString("O") },
            new Uri("http://capacity-callback:8080/"), "capacity-test-credential", deadline, TimeSpan.FromSeconds(15), "service-0", target.ExecutionId, [],
            new Uri("http://capacity-callback:8080/task"), new Dictionary<string, string> { ["service-0"] = host })
            { Requirements = [CheckItemKind.Functionality], ReadyTimeoutSeconds = 1 };
        await Assert.That(await execute.ExecuteAsync(work, ct)).IsEqualTo(AwdCheckerExecutionOutcome.Completed);
        await Assert.That(await execute.ExecuteAsync(work with { ProviderReceipt = target.Receipt with { OperationId = Guid.NewGuid() } }, ct))
            .IsEqualTo(AwdCheckerExecutionOutcome.Superseded);
        await Assert.That(await HasAllocationAsync(fixture, target.ExecutionId, ct)).IsFalse();
        await Assert.That((await fixture.Providers.Runtime(RuntimeProvider.Docker).GetStatusAsync(target.Receipt.ToReceipt(), ct))!.Status).IsEqualTo(RuntimeStatus.Running);
    }

    [Test, Arguments(WaitingResult.Completed), Arguments(WaitingResult.Deadline), Arguments(WaitingResult.CallerCanceled), Timeout(300_000)]
    public async Task A_real_auxiliary_slot_wait_preserves_the_execution_identity_and_original_budget(WaitingResult expected, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var targets = await SeedAsync(fixture, 2, ct);
        await using var callback = new ContainerBuilder(PythonImage).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
            .WithCommand("python", "-m", "http.server", "8080").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080)).Build();
        await callback.StartAsync(ct);
        await fixture.RefreshOwnerAsync(ct, auxiliaryConcurrency: 1);
        using var mutations = new RunnerResourceMutationCoordinator();
        await using var firstDb = await fixture.Contexts.CreateDbContextAsync(ct);
        await using var secondDb = await fixture.Contexts.CreateDbContextAsync(ct);
        ExecuteCheck Executor(NoCTF.Infrastructure.Persistence.NoCtfDbContext db) => new(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions(),
            new AuxiliaryRuntimeCapacity(fixture.CapacityFor(db), fixture.RunnerOptions, fixture.Reconcilers, mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance)));
        var first = Executor(firstDb).ExecuteAsync(Input(targets[0], "sleep 3; wget -qO- \"$NOCTF_CALLBACK_URL\" > /dev/null", TimeSpan.FromSeconds(15)), null, ct);
        for (var attempt = 0; attempt < 100 && !await HasAllocationAsync(fixture, targets[0].ExecutionId, ct); attempt++) await Task.Delay(50, ct);
        await Assert.That(await HasAllocationAsync(fixture, targets[0].ExecutionId, ct)).IsTrue();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (expected == WaitingResult.CallerCanceled) caller.CancelAfter(TimeSpan.FromMilliseconds(250));
        var second = Executor(secondDb).ExecuteAsync(Input(targets[1], "wget -qO- \"$NOCTF_CALLBACK_URL\" > /dev/null",
            expected == WaitingResult.Deadline ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(15)), null, caller.Token);
        if (expected == WaitingResult.CallerCanceled)
            await Assert.That(async () => { await second; }).Throws<OperationCanceledException>();
        else
        {
            var result = await second;
            if (expected == WaitingResult.Completed) await Assert.That(result.Succeeded).IsTrue();
            else await Assert.That(result.FailureCode).IsEqualTo(CapabilityExecutionFailure.DeadlineExceeded);
        }
        await Assert.That((await first).Succeeded).IsTrue();
        await Assert.That(await HasAllocationAsync(fixture, targets[0].ExecutionId, ct)).IsFalse();
        await Assert.That(await HasAllocationAsync(fixture, targets[1].ExecutionId, ct)).IsFalse();
        await Assert.That(await fixture.Db.GameplayFacts.CountAsync(value => value.Awd != null && value.Awd.Attempt == 1, ct)).IsEqualTo(2);
    }

    [Test, Timeout(300_000)]
    public async Task A_node_resource_limit_rejects_the_checker_before_any_provider_workload_is_created(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var target = (await SeedAsync(fixture, 1, ct))[0];
        await fixture.RefreshOwnerAsync(ct, resourceLimit: new(128L * 1024 * 1024, 4000, 4096));
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(fixture.Capacity, fixture.RunnerOptions, fixture.Reconcilers, mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        var result = await new ExecuteCheck(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions(), capacity))
            .ExecuteAsync(Input(target, "exit 0", TimeSpan.FromSeconds(15)), null, ct);
        await Assert.That(result.FailureCode).IsEqualTo(CapabilityExecutionFailure.ResourceUnavailable);
        await Assert.That(await HasAllocationAsync(fixture, target.ExecutionId, ct)).IsFalse();
        await Assert.That(await fixture.Reconcilers.Single().WorkloadExistsAsync(new(RuntimeWorkloadKind.AwdChecker, target.RuntimeId, target.ExecutionId), ct)).IsFalse();
    }

    private static CheckExecutionInput Input(Target target, string script, TimeSpan timeout) => new(target.RuntimeId, RuntimeProvider.Docker,
        RuntimeExecutionFixture.Image, ["/bin/sh", "-c", script], new Dictionary<string, string>(), target.Receipt.ServiceReceipt("service-0"),
        target.Receipt.Services.Single().InternalHost!, 1, new Uri("http://capacity-callback:8080/"), "capacity-test-credential", timeout,
        ScopeId: target.FactId) { Use = CheckExecutionUse.Observation, ExecutionId = target.ExecutionId, Requirements = [CheckItemKind.Functionality] };

    private static Task<bool> HasAllocationAsync(RuntimeExecutionFixture fixture, Guid operationId, CancellationToken ct) =>
        fixture.Db.RuntimeInstances.IgnoreAutoIncludes().SelectMany(value => value.CapacityAllocationEntries)
            .AnyAsync(value => value.OperationId == operationId && value.WorkloadKind == RuntimeWorkloadKind.AwdChecker, ct);

    private static async Task<Target[]> SeedAsync(RuntimeExecutionFixture fixture, int count, CancellationToken ct)
    {
        var db = fixture.Db; var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.Application.TemplateId, ct);
        template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate
            { Services = [new() { Name = "service-0", Image = RuntimeExecutionFixture.Image, InternalPorts = [new() { Port = 8080 }] }] };
        template.Definition.Break!.FlagStores.Add(new() { Id = Guid.NewGuid(), Name = "main", TargetServiceName = "service-0", DeliveryKind = FlagDeliveryKind.BusinessChecker });
        template.Definition.Capabilities.Add(new CheckCapabilityDefinition { Image = RuntimeExecutionFixture.Image, TargetServiceName = "service-0",
            Requirements = [new() { Kind = CheckItemKind.Functionality }] });
        db.Competitions.Add(new AwdCompetition { Id = competitionId, OwnerId = fixture.Application.OwnerId, Title = "Auxiliary admission",
            Status = CompetitionStatus.Running, StartAt = now.AddMinutes(-1), EndAt = now.AddHours(1),
            ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awd, competitionId) });
        db.CompetitionChallenges.Add(new AwdCompetitionChallenge { Id = challengeId, CompetitionId = competitionId,
            ChallengeId = fixture.Application.TemplateId, Rules = new AwdCompetitionChallengeRules(), IsPublished = true });
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = competitionId, Number = 1, StartsAt = now, EndsAt = now.AddMinutes(5),
            ActiveEndTicks = TimeSpan.FromMinutes(5).Ticks };
        var rules = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = competitionId, CompetitionChallengeId = challengeId,
            RoundId = round.Id, EffectiveAt = now, CheckerIntervalSeconds = 60 };
        db.AwdRounds.Add(round); db.AwdRoundRules.Add(rules); await db.SaveChangesAsync(ct);
        var result = new List<Target>();
        for (var index = 0; index < count; index++)
        {
            var userId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var factId = Guid.NewGuid(); var executionId = Guid.NewGuid();
            db.Users.Add(new User { Id = userId, UserName = userId.ToString("N"), NormalizedUserName = userId.ToString("N").ToUpperInvariant(),
                Email = $"{userId:N}@example.test", AccountStatus = UserAccountStatus.Active });
            db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = userId, MemberIds = [userId],
                Name = $"Team {index}", InvitationToken = Guid.NewGuid().ToString("N"), RegistrationStatus = TeamRegistrationStatus.Approved });
            await db.SaveChangesAsync(ct);
            var message = await fixture.QueueAsync(1, ct, new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = competitionId,
                CompetitionChallengeId = challengeId, TeamId = teamId, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Queued, CreatedAt = now });
            var started = await fixture.Handler.ProvisionContainerAsync(message, ct) as NoCTF.Application.Runtime.Instances.RuntimeProvisioned
                ?? throw new InvalidOperationException("The capacity test target did not start.");
            await fixture.PersistAsync(started, ct);
            db.GameplayFacts.Add(new CheckObservationGameplayFact { Id = factId, CompetitionId = competitionId,
                CompetitionChallengeId = challengeId, TeamId = teamId, State = GameplayFactState.Processing, OccurredAt = now, UpdatedAt = now,
                ReferenceKind = GameplayFactReferenceKind.AwdRound, ReferenceId = round.Id,
                Awd = new() { RulesId = rules.Id, RuntimeId = message.RuntimeInstanceId, ReceivedRoundId = round.Id,
                    AttemptId = executionId, Attempt = 1, Execution = AwdCheckExecution.Running,
                    AttemptStartedAt = now, AttemptDeadline = now.AddMinutes(1), Deadline = now.AddMinutes(1), ReceiptDeadline = now.AddMinutes(2) } });
            await db.SaveChangesAsync(ct);
            result.Add(new(message.RuntimeInstanceId, factId, executionId, (ContainerRuntimeReceiptData)started.ProviderReceipt));
        }
        return result.ToArray();
    }

    private sealed record Target(Guid RuntimeId, Guid FactId, Guid ExecutionId, ContainerRuntimeReceiptData Receipt);
}

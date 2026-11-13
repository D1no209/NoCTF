using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class ControlContainerFlowTests
{
    public enum Scenario { Controlled, Uncontrolled, Unavailable, MissingCallback, NonzeroAfterCallback, Timeout, Stopped }

    [Test, Arguments(Scenario.Controlled), Arguments(Scenario.Uncontrolled), Arguments(Scenario.Unavailable),
     Arguments(Scenario.MissingCallback), Arguments(Scenario.NonzeroAfterCallback), Arguments(Scenario.Timeout), Timeout(300_000)]
    public Task Docker_Control_executes_signed_callbacks_and_preserves_the_interactive_target(Scenario scenario, CancellationToken ct) =>
        VerifyAsync(scenario, RuntimeProvider.Docker, ct);

    [Test, Timeout(300_000)]
    public Task Docker_Control_stops_active_workload_when_the_target_is_stopped(CancellationToken ct) =>
        VerifyAsync(Scenario.Stopped, RuntimeProvider.Docker, ct);

    [Test, Timeout(300_000)]
    public Task Kubernetes_Control_callback_identity_and_replays_are_fenced(CancellationToken ct) =>
        VerifyAsync(Scenario.Controlled, RuntimeProvider.Kubernetes, ct);

    [Test, Arguments(Scenario.MissingCallback), Arguments(Scenario.NonzeroAfterCallback),
     Arguments(Scenario.Timeout), Timeout(300_000)]
    public Task Kubernetes_Control_executes_signed_callbacks_and_preserves_the_interactive_target(Scenario scenario, CancellationToken ct) =>
        VerifyAsync(scenario, RuntimeProvider.Kubernetes, ct);

    private static async Task VerifyAsync(Scenario scenario, RuntimeProvider provider, CancellationToken ct)
    {
        if (provider == RuntimeProvider.Kubernetes && Environment.GetEnvironmentVariable("NOCTF_KUBERNETES_INTEGRATION") != "true")
            Skip.Test("Requires an explicitly isolated Kubernetes cluster with actual NetworkPolicy enforcement.");
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct, provider: provider);
        await using var callback = await CapabilityCallbackFixture.CreateAsync(fixture, ct);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        var targetScript = await File.ReadAllTextAsync(Path.Combine(root, "backend/tests/NoCTF.E2E/fixtures/koh-runtime/server.py"), ct);
        var controlScript = await File.ReadAllTextAsync(Path.Combine(root, "backend/tests/NoCTF.E2E/fixtures/koh-control/controller.py"), ct);
        var db = fixture.Db;
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.Application.TemplateId, ct);
        template.Definition = new()
        {
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new()
            {
                Name = "service-0", Image = CapabilityCallbackFixture.PythonImage, CpuCores = .1m, MemoryMiB = 64,
                Commands = [new() { Position = 0, Value = "python" }, new() { Position = 1, Value = "-c" }, new() { Position = 2, Value = targetScript }],
                InternalPorts = [new() { Port = 8080 }]
            }] },
            Capabilities = [new ControlCapabilityDefinition
            {
                Image = CapabilityCallbackFixture.PythonImage, ServiceName = "service-0", TimeoutSeconds = provider == RuntimeProvider.Kubernetes ? 60 : 20,
                Command = [new() { Position = 0, Value = "python" }, new() { Position = 1, Value = "-c" }, new() { Position = 2, Value = controlScript }],
                Environment = scenario is Scenario.MissingCallback or Scenario.NonzeroAfterCallback or Scenario.Stopped
                    ? [new() { Name = "CONTROL_TEST_COMPLETION", Value = scenario == Scenario.MissingCallback ? "missing-callback"
                        : scenario == Scenario.Stopped ? "wait-for-cancel" : "nonzero-after-callback" }] : []
            }]
        };
        await db.SaveChangesAsync(ct);
        var queued = await fixture.QueueAsync(1, ct);
        var definition = (ContainerRuntimeDefinition)new ChallengeRuntimeTemplateCatalog().Get(template.Definition)!.Definition;
        queued = queued with { Definition = queued.Definition with { Services = definition.Services } };
        var provisioned = await fixture.Handler.ProvisionContainerAsync(queued, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The real Control target did not start.");
        await fixture.PersistAsync(provisioned, ct);
        var receipt = ((ContainerRuntimeReceiptData)provisioned.ProviderReceipt).ServiceReceipt("service-0");
        var mode = scenario == Scenario.Unavailable ? "unavailable" : scenario == Scenario.Timeout ? "timeout" : "flag";
        var value = scenario == Scenario.Uncontrolled ? "" : "flag%7Breal-control%7D";
        ContainerExecResult? ready = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            ready = await fixture.Providers.Sandbox(provider).ExecRetainingTargetAsync(receipt, ["python", "-c",
                $"import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/set?mode={mode}&value={value}', timeout=5).read()"], TimeSpan.FromSeconds(30), ct);
            if (ready.TimedOut) throw new InvalidOperationException("Control target setup exceeded its execution budget.");
            if (ready.ExitCode == 0) break;
            await Task.Delay(100, ct);
        }
        if (ready?.ExitCode != 0) throw new InvalidOperationException($"Control target readiness failed: {ready}.");
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = provider, RunnerPool = RuntimeExecutionFixture.Pool }));
        var tests = new TemplateCapabilityTestStore(db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Application.Publisher, TimeProvider.System);
        var accepted = await tests.StartAsync(new(new(template.Id, fixture.Application.OwnerId, false, provisioned.RuntimeInstanceId),
            Guid.NewGuid(), TemplateCapabilityTestKind.Control, null, string.Empty, DateTimeOffset.UtcNow), ct);
        await Assert.That(accepted.Succeeded).IsTrue();
        var id = accepted.Value!.ExecutionId;
        db.ChangeTracker.Clear();
        var test = await db.Set<TemplateCapabilityTest>().SingleAsync(item => item.ExecutionId == id, ct);
        var command = new RunControlObservation(id, provisioned.RuntimeInstanceId, CapabilityExecutionScope.TemplateTest,
            test.ControlExecution!.Deadline, RuntimeExecutionFixture.RunnerId);
        var executions = new ControlExecutionStore(fixture.Contexts, fixture.Application.Publisher, TimeProvider.System);
        using var mutations = new RunnerResourceMutationCoordinator();
        var limits = new RuntimeExecutionOptions { ProcessesPerService = provider == RuntimeProvider.Kubernetes ? 128 : 256 };
        var capacity = new AuxiliaryRuntimeCapacity(fixture.Capacity, fixture.RunnerOptions, fixture.Reconcilers, mutations,
            NullLogger<AuxiliaryRuntimeCapacity>.Instance, limits);
        var tokens = new RunnerScoringTokenIssuer(callback.Scoring);
        using var logs = LoggerFactory.Create(builder => builder.AddConsole());
        var handler = new ControlObservationHandler(executions, fixture.Providers, capacity, fixture.Capacity, mutations, tokens,
            fixture.RunnerOptions, callback.Scoring, limits, TimeProvider.System, fixture.Application.Lifetime,
            logs.CreateLogger<ControlObservationHandler>());
        await fixture.RefreshOwnerAsync(ct);
        var executionTask = handler.Handle(command, ct);
        if (scenario == Scenario.Stopped)
        {
            var workload = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.TemplateChecker, provisioned.RuntimeInstanceId, id);
            using var started = CancellationTokenSource.CreateLinkedTokenSource(ct);
            started.CancelAfter(TimeSpan.FromSeconds(15));
            while (await fixture.Reconcilers.Single().WorkloadExistsAsync(workload, started.Token) != true)
                await Task.Delay(100, started.Token);
            await using var stopping = await fixture.Contexts.CreateDbContextAsync(ct);
            var targetRuntime = await stopping.RuntimeInstances.SingleAsync(item => item.Id == provisioned.RuntimeInstanceId, ct);
            targetRuntime.State = RuntimeState.Stopping;
            targetRuntime.CapabilityTest!.CancellationRequested = true;
            await stopping.SaveChangesAsync(ct);
            await executionTask.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
        else await executionTask;
        await handler.Handle(command, ct);
        await executions.FinalizeAsync(new(id, provisioned.RuntimeInstanceId, CapabilityExecutionScope.TemplateTest), ct);
        db.ChangeTracker.Clear();
        test = await db.Set<TemplateCapabilityTest>().SingleAsync(item => item.ExecutionId == id, ct);
        var failure = scenario switch
        {
            Scenario.Unavailable => CapabilityExecutionFailure.RuntimeUnavailable,
            Scenario.Timeout => CapabilityExecutionFailure.DeadlineExceeded,
            Scenario.Stopped => CapabilityExecutionFailure.TargetSuperseded,
            Scenario.MissingCallback => CapabilityExecutionFailure.MissingEvidence,
            Scenario.NonzeroAfterCallback => CapabilityExecutionFailure.TargetExecutionFailed,
            _ => (CapabilityExecutionFailure?)null
        };
        await Assert.That(test.ControlExecution!.Failure).IsEqualTo(failure);
        if (scenario is Scenario.Controlled or Scenario.NonzeroAfterCallback)
        {
            await Assert.That(test.ControlExecution.Result).IsEqualTo(ControlResult.Controlled);
            await Assert.That(test.ControlExecution.ControllerIdentifier).IsEqualTo("flag{real-control}");
        }
        if (scenario == Scenario.Uncontrolled) await Assert.That(test.ControlExecution.Result).IsEqualTo(ControlResult.Uncontrolled);
        await Assert.That(test.State).IsEqualTo(scenario == Scenario.Stopped ? TemplateCapabilityTestState.Canceled
            : failure is null ? TemplateCapabilityTestState.Completed : TemplateCapabilityTestState.PlatformFailed);
        await Assert.That(test.ControlExecution.FinalizedAt).IsNotNull();
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleAsync(item => item.Id == provisioned.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items.All(item => item.Identity.OperationId != id)).IsTrue();
        await Assert.That((await fixture.Providers.Containers(provider).GetAsync(provider, receipt.ResourceId, ct))!.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        if (scenario is Scenario.MissingCallback or Scenario.Stopped) return;
        using var client = new HttpClient { BaseAddress = callback.PublicBaseUrl };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueControlResult(
            RuntimeExecutionFixture.RunnerId, id, provisioned.RuntimeInstanceId, CapabilityExecutionScope.TemplateTest,
            command.Deadline, DateTimeOffset.UtcNow));
        var evidence = new
        {
            executionState = scenario is Scenario.Unavailable or Scenario.Timeout ? "PlatformFailed" : "Completed",
            result = test.ControlExecution.Result!.Value.ToString(), controllerIdentifier = test.ControlExecution.ControllerIdentifier,
            executionFailure = test.ControlExecution.ReportedFailure?.ToString(), observedAt = test.ControlExecution.ObservedAt
        };
        using var replay = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results", evidence, ct);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var conflict = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results",
            evidence with { controllerIdentifier = "conflicting-controller" }, ct);
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var invalidReplay = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results",
            evidence with { executionState = "NotExecuted" }, ct);
        await Assert.That(invalidReplay.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueControlResult(
            RuntimeExecutionFixture.RunnerId, id, Guid.NewGuid(), CapabilityExecutionScope.TemplateTest, command.Deadline, DateTimeOffset.UtcNow));
        using var foreignRuntime = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results", evidence, ct);
        await Assert.That(foreignRuntime.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueControlResult(
            RuntimeExecutionFixture.RunnerId, id, provisioned.RuntimeInstanceId, CapabilityExecutionScope.TemplateTest,
            expiredAt.AddSeconds(1), expiredAt));
        using var expired = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results", evidence, ct);
        await Assert.That(expired.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        runtime.State = RuntimeState.Stopping;
        await db.SaveChangesAsync(ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.IssueControlResult(
            RuntimeExecutionFixture.RunnerId, id, provisioned.RuntimeInstanceId, CapabilityExecutionScope.TemplateTest,
            command.Deadline, DateTimeOffset.UtcNow));
        using var superseded = await client.PostAsJsonAsync("/api/internal/v1/capabilities/control-results", evidence, ct);
        await Assert.That((await superseded.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct)).GetProperty("disposition").GetString()).IsEqualTo("Superseded");
    }
}

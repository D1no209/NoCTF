using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FluentStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Infrastructure.Storage;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class TemplateFixExecutionTests
{
    [Test, Arguments(true, false), Arguments(false, false), Arguments(true, true), Arguments(false, true), Timeout(300_000)]
    public async Task Fix_runs_in_a_real_isolated_target_once_and_reclaims_capacity(bool succeeds, bool withCheck, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        await using var callback = await CapabilityCallbackFixture.CreateAsync(fixture, ct);
        var db = fixture.Db;
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.Application.TemplateId, ct);
        template.Definition = new()
        {
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new()
            {
                Name = "service-0", Image = RuntimeExecutionFixture.Image, CpuCores = .1m, MemoryMiB = 64,
                Commands = [new() { Position = 0, Value = "/bin/sh" }, new() { Position = 1, Value = "-c" },
                    new() { Position = 2, Value = "printf vulnerable > /repair-state; exec nc -ll -p 8080 -e /bin/cat /repair-state" }],
                InternalPorts = [new() { Port = 8080 }]
            }] },
            Capabilities = [new FixCapabilityDefinition { TargetServiceName = "service-0", Entrypoint = "fix.sh", TimeoutSeconds = 10 }]
        };
        var script = succeeds ? "test ! -e /patch-applied || exit 88\nprintf applied > /patch-applied\nprintf repaired > /repair-state\n" : "exit 17\n";
        if (withCheck) template.Definition.Capabilities.Add(new CheckCapabilityDefinition
        {
            Image = CapabilityCallbackFixture.PythonImage, TargetServiceName = "service-0", AcceptsFixInput = true,
            Command = [new() { Position = 0, Value = "python" }, new() { Position = 1, Value = "-c" },
                new() { Position = 2, Value = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "check_repair.py"), ct) }],
            Environment = [new() { Name = "PATCH_SCRIPT_SHA256", Value = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(script))) }],
            Requirements = [new() { Kind = CheckItemKind.Functionality }, new() { Kind = CheckItemKind.Vulnerability }]
        });
        await db.SaveChangesAsync(ct);
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool }));
        var store = new TemplateCapabilityTestStore(db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Application.Publisher, TimeProvider.System);
        var directory = Path.GetFullPath(Path.Combine("artifacts", "template-fix-execution", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        using var objects = StorageFactory.Disk(Path.Combine(directory, "objects"));
        var uploads = new ManagedFileUploads(new ManagedFileUploadRegistry(db, fixture.Application.Publisher,
            NullLogger<ManagedFileUploadRegistry>.Instance), objects);
        using var archive = Archive(script);
        var accepted = await new StartTemplateCapabilityTest(store, uploads, TimeProvider.System).FixAsync(
            new(template.Id, fixture.Application.OwnerId, false), Guid.NewGuid(), withCheck, "fix.tar.gz", "application/gzip", archive, ct);
        await Assert.That(accepted.Succeeded).IsTrue();
        var result = accepted.Value!;
        var runtimeTemplate = new ChallengeRuntimeTemplateCatalog().Get(template.Definition)!;
        var services = ((ContainerRuntimeDefinition)runtimeTemplate.Definition).Services;
        var request = new ContainerRuntimeRequest(result.RuntimeInstanceId, RuntimeProvider.Docker, services,
            new Dictionary<string, string> { ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = result.RuntimeInstanceId.ToString("D") },
            NoCTF.Application.Runtime.Capacity.RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256))),
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
        var queued = await fixture.ClaimAsync(new(result.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, request), ct);
        var reply = await fixture.Handler.ProvisionContainerAsync(queued, ct);
        if (reply is not RuntimeProvisioned provisioned)
            throw new InvalidOperationException($"The template target did not start: {(reply is RuntimeProvisionFailed failed ? failed.FailureCode.ToString() : "NoRuntimeReceipt")}.");
        await fixture.PersistAsync(provisioned, ct);
        var execution = new TemplateCapabilityTestExecutionStore(fixture.Contexts, fixture.Application.Publisher, TimeProvider.System);
        using var mutations = new RunnerResourceMutationCoordinator();
        var progress = new FixExecutionProgressStore(fixture.Contexts);
        var scoring = callback.Scoring;
        var fixOptions = Options.Create(new FixVerificationOptions { WorkDirectory = Path.Combine(directory, "execution") });
        var handler = new TemplateCapabilityTestHandler(execution,
            new(new RuntimeFixExecutor(fixture.Providers, mutations, fixture.Capacity, fixture.RunnerOptions, progress, TimeProvider.System), TimeProvider.System),
            new(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions())), objects, new(fixOptions),
            new RunnerScoringTokenIssuer(scoring), fixture.RunnerOptions, scoring, fixOptions, fixture.Capacity, mutations,
            TimeProvider.System, fixture.Application.Lifetime, NullLogger<TemplateCapabilityTestHandler>.Instance);
        await fixture.RefreshOwnerAsync(ct);
        var command = new RunTemplateCapabilityTest(result.RuntimeInstanceId, result.ExecutionId, RuntimeExecutionFixture.RunnerId);
        await handler.Handle(command, ct);
        await handler.Handle(command, ct);
        db.ChangeTracker.Clear();
        var test = await db.Set<TemplateCapabilityTest>().SingleAsync(value => value.ExecutionId == result.ExecutionId, ct);
        await Assert.That(test.FixResult).IsEqualTo(succeeds ? FixResult.Applied : FixResult.Failed);
        await Assert.That(test.Failure).IsNull();
        await Assert.That(test.CheckCompleted).IsEqualTo(withCheck && succeeds);
        await Assert.That(test.Probes.Count).IsEqualTo(withCheck && succeeds ? 2 : 0);
        await Assert.That(test.State).IsEqualTo(TemplateCapabilityTestState.Finalizing);
        var receipt = ((ContainerRuntimeReceiptData)provisioned.ProviderReceipt).ServiceReceipt("service-0");
        var marker = await fixture.Containers.ExecAsync(receipt, ["/bin/sh", "-c", succeeds ? "test -f /patch-applied" : "test ! -e /patch-applied"], TimeSpan.FromSeconds(5), ct);
        await Assert.That(marker.ExitCode).IsEqualTo(0);
        await execution.DispatchAsync(new(result.RuntimeInstanceId, result.ExecutionId), ct);
        db.ChangeTracker.Clear();
        var stopped = await fixture.Handler.StopContainerAsync(new(result.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, DateTimeOffset.UtcNow), ct);
        await Assert.That(stopped).IsTypeOf<RuntimeStopped>();
        db.ChangeTracker.Clear();
        await new RuntimeStopWriteBackMessageHandler(db, fixture.Application.Publisher,
            new CompetitionEventStore(db, fixture.Application.Publisher), TimeProvider.System).Handle((RuntimeStopped)stopped, ct);
        await execution.DispatchAsync(new(result.RuntimeInstanceId, result.ExecutionId), ct);
        db.ChangeTracker.Clear();
        var finished = await db.Set<TemplateCapabilityTest>().SingleAsync(value => value.ExecutionId == result.ExecutionId, ct);
        await Assert.That(finished.State).IsEqualTo(TemplateCapabilityTestState.Completed);
        await Assert.That(finished.FileId).IsNull();
        await Assert.That(await fixture.Containers.GetAsync(RuntimeProvider.Docker, receipt.ResourceId, ct)).IsNull();
        await Assert.That((await db.RuntimeInstances.SingleAsync(value => value.Id == result.RuntimeInstanceId, ct)).CapacityAllocations.Items.Count).IsEqualTo(0);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
    }

    [Test, Timeout(300_000)]
    public async Task Check_preserves_interactive_target_and_accepts_only_idempotent_signed_evidence(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        await using var callback = await CapabilityCallbackFixture.CreateAsync(fixture, ct);
        var db = fixture.Db;
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.Application.TemplateId, ct);
        template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Services = [new()
            { Name = "service-0", Image = RuntimeExecutionFixture.Image, InternalPorts = [new() { Port = 8080 }] }] };
        template.Definition.Capabilities = [new CheckCapabilityDefinition
        {
            Image = CapabilityCallbackFixture.PythonImage, TargetServiceName = "service-0",
            Command = [new() { Position = 0, Value = "python" }, new() { Position = 1, Value = "-c" },
                new() { Position = 2, Value = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "check_template.py"), ct) }],
            Requirements = [new() { Kind = CheckItemKind.Functionality }]
        }];
        await db.SaveChangesAsync(ct);
        var queued = await fixture.QueueAsync(1, ct);
        var provisioned = await fixture.Handler.ProvisionContainerAsync(queued, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The interactive Check target did not start.");
        await fixture.PersistAsync(provisioned, ct);
        var placement = new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions
            { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool }));
        var store = new TemplateCapabilityTestStore(db, new ChallengeRuntimeTemplateCatalog(), placement, fixture.Application.Publisher, TimeProvider.System);
        var directory = Path.GetFullPath(Path.Combine("artifacts", "template-check-execution", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        using var objects = StorageFactory.Disk(directory);
        var uploads = new ManagedFileUploads(new ManagedFileUploadRegistry(db, fixture.Application.Publisher,
            NullLogger<ManagedFileUploadRegistry>.Instance), objects);
        var access = new ChallengeTestAccess(template.Id, fixture.Application.OwnerId, false, provisioned.RuntimeInstanceId);
        var accepted = await new StartTemplateCapabilityTest(store, uploads, TimeProvider.System).CheckAsync(access, Guid.NewGuid(), ct);
        await Assert.That(accepted.Succeeded).IsTrue();
        await Assert.That(accepted.Value!.RuntimeInstanceId).IsEqualTo(provisioned.RuntimeInstanceId);
        var execution = new TemplateCapabilityTestExecutionStore(fixture.Contexts, fixture.Application.Publisher, TimeProvider.System);
        using var mutations = new RunnerResourceMutationCoordinator();
        var options = Options.Create(new FixVerificationOptions { WorkDirectory = Path.Combine(directory, "execution") });
        var handler = new TemplateCapabilityTestHandler(execution,
            new(new RuntimeFixExecutor(fixture.Providers, mutations, fixture.Capacity, fixture.RunnerOptions,
                new FixExecutionProgressStore(fixture.Contexts), TimeProvider.System), TimeProvider.System),
            new(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions())), objects, new(options),
            new RunnerScoringTokenIssuer(callback.Scoring), fixture.RunnerOptions, callback.Scoring, options, fixture.Capacity,
            mutations, TimeProvider.System, fixture.Application.Lifetime, NullLogger<TemplateCapabilityTestHandler>.Instance);
        await fixture.RefreshOwnerAsync(ct);
        var command = new RunTemplateCapabilityTest(provisioned.RuntimeInstanceId, accepted.Value.ExecutionId, RuntimeExecutionFixture.RunnerId);
        await handler.Handle(command, ct);
        await handler.Handle(command, ct);
        await execution.DispatchAsync(new(command.RuntimeInstanceId, command.ExecutionId), ct);
        db.ChangeTracker.Clear();
        var current = await db.Set<TemplateCapabilityTest>().SingleAsync(value => value.ExecutionId == command.ExecutionId, ct);
        await Assert.That(current.State).IsEqualTo(TemplateCapabilityTestState.Completed);
        await Assert.That(current.Failure).IsNull();
        await Assert.That(current.CheckCompleted).IsTrue();
        await Assert.That(current.Probes.Single().Result).IsEqualTo(CheckProbeResult.Passed);
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == provisioned.RuntimeInstanceId, ct);
        await Assert.That(runtime.State).IsEqualTo(RuntimeState.Running);
        await Assert.That(runtime.CapacityAllocations.Items.All(value => !value.Identity.IsAuxiliary)).IsTrue();
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await execution.RecordCheckAsync(Guid.NewGuid(), command.ExecutionId, RuntimeExecutionFixture.RunnerId,
            [], [], DateTimeOffset.UtcNow, ct)).IsEqualTo(NoCTF.Application.GameplayFacts.Processing.InternalResultDisposition.NotFound);
    }

    private static MemoryStream Archive(string script)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, true))
        using (var content = new MemoryStream(Encoding.UTF8.GetBytes(script)))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = content });
        output.Position = 0; return output;
    }
}

using Microsoft.EntityFrameworkCore;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Runner.Messages;
using NoCTF.Runner.Composition;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class RuntimeProviderDockerTests
{
    [Test, Arguments(RuntimeFailureCode.InvalidConfiguration), Arguments(RuntimeFailureCode.ProviderRejected), Arguments(RuntimeFailureCode.UrlExpansionFailed), Timeout(300_000)]
    public async Task Actual_provision_failures_keep_their_classification_and_release_only_cleaned_resources(RuntimeFailureCode failure, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct, failure == RuntimeFailureCode.UrlExpansionFailed ? "" : "localhost");
        var message = await fixture.QueueAsync(1, ct);
        if (failure == RuntimeFailureCode.InvalidConfiguration)
            message = message with { Definition = message.Definition with { UrlBindings = [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080, "missing")] } };
        if (failure == RuntimeFailureCode.ProviderRejected)
            message = message with { Definition = message.Definition with { Services = [message.Definition.Services[0] with { Command = ["/bin/sh", "-c", "exit 17"] }] } };
        var health = new RunnerProviderHealthState(fixture.RunnerOptions, TimeProvider.System, NullLogger<RunnerProviderHealthState>.Instance);
        var handler = new RuntimeProviderHandler(fixture.Providers, fixture.Reconcilers, fixture.RunnerOptions, fixture.Capacity,
            new RuntimeNodeWorkReader(fixture.Contexts, TimeProvider.System), providerHealth: health);
        var result = await handler.ProvisionContainerAsync(message, ct);
        await Assert.That(result).IsTypeOf<RuntimeProvisionTerminated>();
        await Assert.That(((RuntimeProvisionTerminated)result!).FailureCode).IsEqualTo(failure);
        await Assert.That(health.IsReady(RuntimeProvider.Docker)).IsTrue();
        fixture.Db.ChangeTracker.Clear();
        var runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
        await Assert.That((await fixture.Reconcilers.Single().ListManagedAsync(ct)).Contains(new RuntimeResourceIdentity(runtime.Id))).IsFalse();
    }

    [Test, Arguments(true), Arguments(false), Timeout(300_000)]
    public async Task Ownership_conflict_never_releases_the_current_capacity_owner(bool createResources, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var message = await fixture.QueueAsync(1, ct);
        if (createResources)
        {
            var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as RuntimeProvisioned
                ?? throw new InvalidOperationException("The ownership conflict target did not start.");
            await fixture.PersistAsync(provisioned, ct);
        }
        var runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        runtime.State = RuntimeState.Stopping;
        runtime.CapacityAllocationEntries.Single().RunnerId = "replacement-owner";
        await fixture.Db.SaveChangesAsync(ct);
        if (createResources)
            await Assert.That(await fixture.Handler.StopContainerAsync(new(runtime.Id, RuntimeExecutionFixture.RunnerId), ct)).IsTypeOf<RuntimeStopFailed>();
        else
            await Assert.That(() => fixture.Handler.ProvisionContainerAsync(message, ct)).Throws<InvalidOperationException>();
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items.Single().RunnerId).IsEqualTo("replacement-owner");
    }

    [Test, Timeout(300_000)]
    public async Task Receipt_for_another_runtime_cannot_delete_the_current_service(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var message = await fixture.QueueAsync(1, ct);
        var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The receipt scope target did not start.");
        await fixture.PersistAsync(provisioned, ct);
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        runtime.State = RuntimeState.Stopping;
        ((ContainerRuntimeReceipt)runtime.ProviderReceipt!).OperationId = Guid.NewGuid();
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(await fixture.Handler.StopContainerAsync(new(runtime.Id, RuntimeExecutionFixture.RunnerId), ct)).IsTypeOf<RuntimeStopFailed>();
        var receipt = (ContainerRuntimeReceiptData)provisioned.ProviderReceipt;
        await Assert.That((await fixture.Providers.Runtime(RuntimeProvider.Docker).GetStatusAsync(receipt.ToReceipt(), ct))!.Status).IsEqualTo(RuntimeStatus.Running);
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).HasSingleItem();
    }

    [Test, Arguments(true), Arguments(false), Timeout(300_000)]
    public async Task Force_termination_reconciles_the_real_resource_identity_with_or_without_a_pending_stop(bool pendingStop, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var message = await fixture.QueueAsync(1, ct);
        var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The force termination target did not start.");
        await fixture.PersistAsync(provisioned, ct);
        if (pendingStop)
        {
            (await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct)).State = RuntimeState.Stopping;
            await fixture.Db.SaveChangesAsync(ct);
        }
        var result = await fixture.Handler.ForceTerminateAsync(new(message.RuntimeInstanceId, RuntimeProvider.Docker,
            RuntimeExecutionFixture.RunnerId, fixture.Application.OwnerId, "Integration cleanup", DateTimeOffset.UtcNow), ct);
        await Assert.That(result).IsTypeOf<RuntimeForceTerminated>();
        fixture.Db.ChangeTracker.Clear();
        var runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
        await Assert.That((await fixture.Reconcilers.Single().ListManagedAsync(ct)).Contains(new RuntimeResourceIdentity(runtime.Id))).IsFalse();
    }

    [Test, Arguments(true), Arguments(false), Timeout(300_000)]
    public async Task Cleanup_retains_capacity_until_an_owned_network_is_actually_absent(bool receiptAvailable, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        fixture.RunnerOptions.Value.Cleanup.NetworkCleanupTimeoutSeconds = 1;
        fixture.RunnerOptions.Value.Cleanup.VerificationTimeoutSeconds = 1;
        var message = await fixture.QueueAsync(2, ct);
        var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The cleanup target did not start.");
        await fixture.PersistAsync(provisioned, ct);
        var receipt = (ContainerRuntimeReceiptData)provisioned.ProviderReceipt;
        await using var retainedEndpoint = new ContainerBuilder(RuntimeExecutionFixture.Image).WithNetwork(receipt.OwnedNetworkId!)
            .WithCommand("sleep", "3600").Build();
        await retainedEndpoint.StartAsync(ct);
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        runtime.State = RuntimeState.Stopping;
        if (!receiptAvailable) runtime.ProviderReceipt = null;
        await fixture.Db.SaveChangesAsync(ct);
        var stop = new StopContainerRuntime(message.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, DateTimeOffset.UtcNow);
        await Assert.That(await fixture.Handler.StopContainerAsync(stop, ct)).IsTypeOf<RuntimeStopFailed>();
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).HasSingleItem();
        await retainedEndpoint.DisposeAsync();
        var completed = await fixture.Handler.StopContainerAsync(stop, ct);
        await Assert.That(completed).IsTypeOf<RuntimeStopped>();
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
    }

    [Test, Timeout(300_000)]
    public async Task Stop_before_creation_cancels_without_creating_provider_resources(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var message = await fixture.QueueAsync(1, ct);
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        runtime.State = RuntimeState.Stopping; await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(await fixture.Handler.ProvisionContainerAsync(message, ct)).IsTypeOf<RuntimeProvisionCanceled>();
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
        var resources = await fixture.Reconcilers.Single().ListManagedAsync(ct);
        await Assert.That(resources.Contains(new RuntimeResourceIdentity(runtime.Id))).IsFalse();
    }

    [Test, Arguments(1), Arguments(2), Timeout(300_000)]
    public async Task Provision_replay_and_stop_use_real_capacity_fences_and_owned_networks(int serviceCount, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var message = await fixture.QueueAsync(serviceCount, ct);
        var result = await fixture.Handler.ProvisionContainerAsync(message, ct);
        var provisioned = result as RuntimeProvisioned ?? throw new InvalidOperationException($"Provision did not complete: {result}.");
        var receipt = (ContainerRuntimeReceiptData)provisioned.ProviderReceipt;
        await Assert.That(receipt.Services.Count).IsEqualTo(serviceCount);
        await Assert.That(receipt.OwnedNetworkId is not null).IsEqualTo(serviceCount > 1);
        await Assert.That(provisioned.PublishedPorts!.Single().HostPort).IsGreaterThan(0);
        await fixture.PersistAsync(provisioned, ct);
        await Assert.That(await fixture.Handler.ProvisionContainerAsync(message, ct)).IsNull();
        await Assert.That(await fixture.Capacity.CanMutateAsync(message.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, ct)).IsTrue();
        await Assert.That(await fixture.Capacity.CanMutateAsync(message.RuntimeInstanceId, "other-runner", ct)).IsFalse();
        if (serviceCount > 1)
        {
            var discovered = await fixture.Providers.Runtime(RuntimeProvider.Docker).ExecAsync(receipt.ToReceipt(), "service-0",
                ["/bin/sh", "-c", "printf 'ready\n' | nc -w 1 service-1 8080 | grep -qx ready"], TimeSpan.FromSeconds(5), ct);
            await Assert.That(discovered.ExitCode).IsEqualTo(0);
        }
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        runtime.State = RuntimeState.Stopping; await fixture.Db.SaveChangesAsync(ct);
        var stopped = await fixture.Handler.StopContainerAsync(new(message.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, DateTimeOffset.UtcNow), ct);
        await Assert.That(stopped).IsTypeOf<RuntimeStopped>();
        await new RuntimeStopWriteBackMessageHandler(fixture.Db, fixture.Application.Publisher,
            new CompetitionEventStore(fixture.Db, fixture.Application.Publisher), TimeProvider.System).Handle((RuntimeStopped)stopped, ct);
        fixture.Db.ChangeTracker.Clear();
        runtime = await fixture.Db.RuntimeInstances.AsSplitQuery().SingleAsync(value => value.Id == message.RuntimeInstanceId, ct);
        await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopped);
        await Assert.That(runtime.CapacityAllocations.Items).IsEmpty();
        var status = await fixture.Providers.Runtime(RuntimeProvider.Docker).GetStatusAsync(receipt.ToReceipt(), ct);
        await Assert.That(status!.Services.All(value => value.Status == RuntimeStatus.Stopped)).IsTrue();
    }
}

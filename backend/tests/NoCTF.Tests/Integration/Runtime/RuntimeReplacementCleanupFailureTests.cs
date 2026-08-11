using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Runner.Messages;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimeReplacementCleanupFailureTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Force_termination_completion_stops_the_old_instance_and_dispatches_replacement(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(
                options,
                includeReplacement: true,
                includeUnrelated: false,
                cancellationToken);
            var outbox = new RecordingOutbox();
            var events = new RecordingCompetitionEventRecorder();
            var completedAt = fixture.Now.AddMinutes(6);

            await using (var db = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeForceTerminated(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "runner-a",
                        fixture.UserId,
                        "The runtime exceeded the cleanup timeout.",
                        fixture.Now.AddMinutes(5),
                        completedAt,
                        RuntimeCleanupResult.ResourcesAbsent),
                    db,
                    outbox,
                    cancellationToken,
                    events);
            }

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(old.StoppedAt).IsEqualTo(completedAt);
            await Assert.That(outbox.Published.OfType<DispatchRuntime>().Single())
                .IsEqualTo(new DispatchRuntime(
                    fixture.ReplacementRuntimeId,
                    fixture.ReplacementProcessingVersion));
            var completed = events.Drafts.Single();
            await Assert.That(completed.Kind)
                .IsEqualTo(NoCTF.Domain.Competitions.Events.CompetitionEventKind.RuntimeForceTerminationCompleted);
            await Assert.That(completed.RuntimeCleanupResult)
                .IsEqualTo(RuntimeCleanupResult.ResourcesAbsent);
            await Assert.That(completed.ActorUserId).IsEqualTo(fixture.UserId);
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Stop_then_stop_preserves_the_cancel_acknowledgement_fence(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: false, includeUnrelated: false,
                cancellationToken);
            await PrepareProvisioningAsync(options, fixture, cancellationToken);
            var outbox = new RecordingOutbox();

            var first = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Stop,
                outbox,
                cancellationToken);
            var replay = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Stop,
                outbox,
                cancellationToken);

            await Assert.That(first.Failure).IsNull();
            await Assert.That(first.Runtime!.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(first.Runtime.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            await Assert.That(replay.Failure).IsNull();
            await Assert.That(replay.Runtime!.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(replay.Runtime.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            var stop = outbox.Published.OfType<StopRuntime>().Single();
            await Assert.That(stop.RuntimeInstanceId).IsEqualTo(fixture.OldRuntimeId);
            await Assert.That(stop.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));

            await using (var ackDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionCanceled(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a"),
                    ackDb,
                    outbox,
                    cancellationToken);

            await using var verify = new NoCtfDbContext(options);
            var stopped = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            await Assert.That(stopped.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopped.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 2));
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Stop_then_reset_preserves_the_terminated_acknowledgement_fence(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: false, includeUnrelated: false,
                cancellationToken);
            await PrepareProvisioningAsync(options, fixture, cancellationToken);
            var outbox = new RecordingOutbox();

            var stop = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Stop,
                outbox,
                cancellationToken);
            var reset = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Reset,
                outbox,
                cancellationToken);

            await Assert.That(stop.Failure).IsNull();
            await Assert.That(stop.Runtime!.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            await Assert.That(reset.Runtime).IsNull();
            await Assert.That(reset.Failure).IsEqualTo(RuntimeMutationFailure.InvalidState);
            var stopMessage = outbox.Published.OfType<StopRuntime>().Single();
            await Assert.That(stopMessage.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));

            await AddQueuedReplacementAsync(options, fixture, cancellationToken);
            await using (var ackDb = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeProvisionTerminated(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.ProviderRejected),
                    ackDb,
                    outbox,
                    cancellationToken);

            var dispatch = outbox.Published.OfType<DispatchRuntime>().Single();
            await Assert.That(dispatch.RuntimeInstanceId)
                .IsEqualTo(fixture.ReplacementRuntimeId);
            await Assert.That(dispatch.ProcessingVersion)
                .IsEqualTo(fixture.ReplacementProcessingVersion);
            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);
            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(old.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 2));
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(replacement.ReplacesRuntimeInstanceId)
                .IsEqualTo(fixture.OldRuntimeId);
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Start_retries_the_failed_receipt_ancestor_before_dispatching_the_new_generation(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);

            await using (var failureDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopFailed(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.CleanupFailed),
                    failureDb,
                    cancellationToken);
            }

            var outbox = new RecordingOutbox();
            var start = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Start,
                outbox,
                cancellationToken);

            await Assert.That(start.Failure).IsNull();
            await Assert.That(start.Runtime).IsNotNull();
            await Assert.That(start.Runtime!.Generation).IsEqualTo(3);
            await Assert.That(start.Runtime.State).IsEqualTo(RuntimeState.Queued);
            var stop = outbox.Published.OfType<StopRuntime>().Single();
            await Assert.That(stop.RuntimeInstanceId).IsEqualTo(fixture.OldRuntimeId);
            await Assert.That(stop.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 2));
            await Assert.That(outbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);
            var retry = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == start.Runtime!.Id,
                cancellationToken);

            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(old.FailureCode).IsNull();
            await Assert.That(old.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 2));
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(old.ProviderReceiptJson!),
                JsonNode.Parse(fixture.ProviderReceiptJson))).IsTrue();
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(replacement.ProviderReceiptJson).IsNull();
            await Assert.That(retry.ReplacesRuntimeInstanceId).IsEqualTo(fixture.OldRuntimeId);
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Reset_rejects_a_queued_replacement_waiting_for_ancestor_cleanup(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);
            var outbox = new RecordingOutbox();

            var reset = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Reset,
                outbox,
                cancellationToken);

            await Assert.That(reset.Runtime).IsNull();
            await Assert.That(reset.Failure).IsEqualTo(RuntimeMutationFailure.InvalidState);
            await Assert.That(outbox.Published).IsEmpty();

            await using var verify = new NoCtfDbContext(options);
            var runtimes = await verify.RuntimeInstances.AsNoTracking()
                .Where(instance =>
                    instance.CompetitionChallengeId == fixture.CompetitionChallengeId &&
                    instance.TeamId == fixture.TeamId)
                .OrderBy(instance => instance.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(runtimes).Count().IsEqualTo(2);
            await Assert.That(runtimes[0].State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(runtimes[0].ProcessingVersion)
                .IsEqualTo(fixture.OldProcessingVersion);
            await Assert.That(runtimes[1].State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(runtimes[1].ProcessingVersion)
                .IsEqualTo(fixture.ReplacementProcessingVersion);
            await Assert.That(runtimes[1].ReplacesRuntimeInstanceId)
                .IsEqualTo(fixture.OldRuntimeId);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Start_retries_the_failed_receipt_ancestor_when_the_latest_replacement_was_stopped(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);
            var stopOutbox = new RecordingOutbox();
            var stop = await MutateAsync(
                options,
                fixture,
                useAdminStore: false,
                RuntimeAction.Stop,
                stopOutbox,
                cancellationToken);
            await Assert.That(stop.Failure).IsNull();
            await Assert.That(stop.Runtime!.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(stopOutbox.Published).IsEmpty();

            await using (var failureDb = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopFailed(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.CleanupFailed),
                    failureDb,
                    cancellationToken);
            }

            var startOutbox = new RecordingOutbox();
            var start = await MutateAsync(
                options,
                fixture,
                useAdminStore: false,
                RuntimeAction.Start,
                startOutbox,
                cancellationToken);

            await Assert.That(start.Failure).IsNull();
            await Assert.That(start.Runtime!.Generation).IsEqualTo(3);
            var cleanup = startOutbox.Published.OfType<StopRuntime>().Single();
            await Assert.That(cleanup.RuntimeInstanceId).IsEqualTo(fixture.OldRuntimeId);
            await Assert.That(startOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);
            var retry = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == start.Runtime!.Id,
                cancellationToken);
            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(old.FailureCode).IsNull();
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(retry.ReplacesRuntimeInstanceId).IsEqualTo(fixture.OldRuntimeId);
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Start_rejects_while_a_stopping_ancestor_remains(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);
            var outbox = new RecordingOutbox();

            var stoppedReplacement = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Stop,
                outbox,
                cancellationToken);
            var start = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Start,
                outbox,
                cancellationToken);

            await Assert.That(stoppedReplacement.Failure).IsNull();
            await Assert.That(stoppedReplacement.Runtime!.State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(start.Runtime).IsNull();
            await Assert.That(start.Failure).IsEqualTo(RuntimeMutationFailure.InvalidState);
            await Assert.That(outbox.Published).IsEmpty();
            await using var verify = new NoCtfDbContext(options);
            var runtimes = await verify.RuntimeInstances.AsNoTracking()
                .Where(instance =>
                    instance.CompetitionChallengeId == fixture.CompetitionChallengeId &&
                    instance.TeamId == fixture.TeamId)
                .OrderBy(instance => instance.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(runtimes).Count().IsEqualTo(2);
            await Assert.That(runtimes[0].State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(runtimes[0].ProcessingVersion)
                .IsEqualTo(fixture.OldProcessingVersion);
            await Assert.That(runtimes[1].State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(runtimes.Any(instance => instance.State == RuntimeState.Queued))
                .IsFalse();
        }, cancellationToken);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Concurrent_stop_ack_after_start_reads_stopping_leaves_no_queued_runtime(
        bool useAdminStore,
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);
            var stoppedReplacement = await MutateAsync(
                options,
                fixture,
                useAdminStore,
                RuntimeAction.Stop,
                new RecordingOutbox(),
                cancellationToken);
            await Assert.That(stoppedReplacement.Failure).IsNull();
            await Assert.That(stoppedReplacement.Runtime!.State).IsEqualTo(RuntimeState.Stopped);

            string connectionString;
            await using (var source = new NoCtfDbContext(options))
                connectionString = source.Database.GetConnectionString()
                    ?? throw new InvalidOperationException("PostgreSQL connection is unavailable.");
            var barrier = new StoppingAncestorReadBarrier();
            var startOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(barrier)
                .Options;
            var startOutbox = new RecordingOutbox();
            var startTask = MutateAsync(
                startOptions,
                fixture,
                useAdminStore,
                RuntimeAction.Start,
                startOutbox,
                cancellationToken);

            try
            {
                await barrier.WaitUntilReadAsync(cancellationToken);
                var ackOutbox = new RecordingOutbox();
                await using (var ackDb = new NoCtfDbContext(options))
                    await RuntimeWriteBackHandler.Handle(
                        new RuntimeStopped(
                            fixture.OldRuntimeId,
                            fixture.OldProcessingVersion,
                            1,
                            "pool-a",
                            "runner-a"),
                        ackDb,
                        ackOutbox,
                        cancellationToken);
                await Assert.That(ackOutbox.Published.OfType<DispatchRuntime>()).IsEmpty();
            }
            finally
            {
                barrier.Release();
            }

            var start = await startTask;
            await Assert.That(start.Runtime).IsNull();
            await Assert.That(start.Failure).IsEqualTo(RuntimeMutationFailure.InvalidState);
            await Assert.That(startOutbox.Published).IsEmpty();

            await using var verify = new NoCtfDbContext(options);
            var runtimes = await verify.RuntimeInstances.AsNoTracking()
                .Where(instance =>
                    instance.CompetitionChallengeId == fixture.CompetitionChallengeId &&
                    instance.TeamId == fixture.TeamId)
                .OrderBy(instance => instance.Generation)
                .ToListAsync(cancellationToken);
            await Assert.That(runtimes).Count().IsEqualTo(2);
            await Assert.That(runtimes[0].State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(runtimes[1].State).IsEqualTo(RuntimeState.Stopped);
            await Assert.That(runtimes.Any(instance => instance.State == RuntimeState.Queued))
                .IsFalse();
        }, cancellationToken);
    }

    private sealed class StoppingAncestorReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> read =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int intercepted;

        public Task WaitUntilReadAsync(CancellationToken cancellationToken) =>
            read.Task.WaitAsync(cancellationToken);

        public void Release() => release.TrySetResult(true);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.TrimStart().StartsWith(
                    "SELECT EXISTS",
                    StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(
                    "runtime_instances",
                    StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(
                    "state",
                    StringComparison.OrdinalIgnoreCase)
                || Interlocked.Exchange(ref intercepted, 1) != 0)
                return result;

            read.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task Cleanup_failure_fails_the_old_runtime_and_its_queued_replacement(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopFailed(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.CleanupFailed),
                    db,
                    cancellationToken);
            }

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);

            await Assert.That(old.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(old.FailureCode).IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(old.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            await Assert.That(JsonNode.DeepEquals(
                JsonNode.Parse(old.ProviderReceiptJson!),
                JsonNode.Parse(fixture.ProviderReceiptJson))).IsTrue();
            await Assert.That(old.RunnerId).IsEqualTo("runner-a");
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(replacement.FailureCode).IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(replacement.ProcessingVersion)
                .IsEqualTo(checked(fixture.ReplacementProcessingVersion + 1));
            await Assert.That(replacement.ReplacesRuntimeInstanceId)
                .IsEqualTo(fixture.OldRuntimeId);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Cleanup_failure_replay_does_not_increment_terminal_versions_again(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);
            var message = new RuntimeStopFailed(
                fixture.OldRuntimeId,
                fixture.OldProcessingVersion,
                1,
                "pool-a",
                "runner-a",
                RuntimeFailureCode.CleanupFailed);

            await using (var first = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(message, first, cancellationToken);

            await using (var replay = new NoCtfDbContext(options))
                await RuntimeWriteBackHandler.Handle(message, replay, cancellationToken);

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);

            await Assert.That(old.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(old.FailureCode).IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(old.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(replacement.FailureCode).IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(replacement.ProcessingVersion)
                .IsEqualTo(checked(fixture.ReplacementProcessingVersion + 1));
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Standalone_cleanup_failure_does_not_touch_an_unrelated_queued_runtime(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: false, includeUnrelated: true,
                cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopFailed(
                        fixture.OldRuntimeId,
                        fixture.OldProcessingVersion,
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.CleanupFailed),
                    db,
                    cancellationToken);
            }

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var unrelated = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.UnrelatedRuntimeId,
                cancellationToken);

            await Assert.That(old.State).IsEqualTo(RuntimeState.Failed);
            await Assert.That(old.FailureCode).IsEqualTo(RuntimeFailureCode.CleanupFailed);
            await Assert.That(old.ProcessingVersion)
                .IsEqualTo(checked(fixture.OldProcessingVersion + 1));
            await Assert.That(unrelated.State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(unrelated.FailureCode).IsNull();
            await Assert.That(unrelated.ProcessingVersion)
                .IsEqualTo(fixture.UnrelatedProcessingVersion);
        }, cancellationToken);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Stale_cleanup_failure_does_not_touch_the_old_runtime_or_replacement(
        CancellationToken cancellationToken)
    {
        await RunAsync(async options =>
        {
            var fixture = await SeedAsync(options, includeReplacement: true, includeUnrelated: false,
                cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                await RuntimeWriteBackHandler.Handle(
                    new RuntimeStopFailed(
                        fixture.OldRuntimeId,
                        checked(fixture.OldProcessingVersion - 1),
                        1,
                        "pool-a",
                        "runner-a",
                        RuntimeFailureCode.CleanupFailed),
                    db,
                    cancellationToken);
            }

            await using var verify = new NoCtfDbContext(options);
            var old = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.OldRuntimeId,
                cancellationToken);
            var replacement = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                instance => instance.Id == fixture.ReplacementRuntimeId,
                cancellationToken);

            await Assert.That(old.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(old.FailureCode).IsNull();
            await Assert.That(old.ProcessingVersion).IsEqualTo(fixture.OldProcessingVersion);
            await Assert.That(replacement.State).IsEqualTo(RuntimeState.Queued);
            await Assert.That(replacement.FailureCode).IsNull();
            await Assert.That(replacement.ProcessingVersion)
                .IsEqualTo(fixture.ReplacementProcessingVersion);
        }, cancellationToken);
    }

    private static async Task RunAsync(
        Func<DbContextOptions<NoCtfDbContext>, Task> test,
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_runtime_replacement_cleanup")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var migration = new NoCtfDbContext(options))
                await migration.Database.MigrateAsync(cancellationToken);
            await test(options);
        });
    }

    private static async Task<RuntimeMutationResult> MutateAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        bool useAdminStore,
        RuntimeAction action,
        RecordingOutbox outbox,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        if (useAdminStore)
        {
            var store = new AdminRuntimeStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                new FixedRuntimePlacementPolicy(),
                new PostgresPerTeamRuntimeFlagStore(db),
                outbox);
            return await store.MutateAsync(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.TeamId,
                action,
                null,
                fixture.Now.AddMinutes(1),
                cancellationToken);
        }

        var playerStore = new RuntimeInstanceStore(
            db,
            new ChallengeRuntimeTemplateCatalog(),
            new FixedRuntimePlacementPolicy(),
            new PostgresPerTeamRuntimeFlagStore(db),
            outbox);
        return await playerStore.MutatePlayerRuntimeAsync(
            new RuntimeMutationCommand(
                fixture.CompetitionId,
                fixture.CompetitionChallengeId,
                fixture.UserId,
                action,
                null,
                fixture.Now.AddMinutes(1)),
            cancellationToken);
    }

    private static async Task PrepareProvisioningAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtime = await db.RuntimeInstances.SingleAsync(
            instance => instance.Id == fixture.OldRuntimeId,
            cancellationToken);
        runtime.State = RuntimeState.Provisioning;
        runtime.ProviderReceiptJson = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task AddQueuedReplacementAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = fixture.ReplacementRuntimeId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.TeamId,
            Purpose = RuntimePurpose.Player,
            Generation = 2,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "pool-a",
            State = RuntimeState.Queued,
            ProcessingVersion = fixture.ReplacementProcessingVersion,
            ReplacesRuntimeInstanceId = fixture.OldRuntimeId,
            CreatedAt = fixture.Now.AddSeconds(1)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        bool includeReplacement,
        bool includeUnrelated,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var observedAt = DateTimeOffset.UtcNow;
        var now = observedAt.AddTicks(
            -(observedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var oldRuntimeId = Guid.CreateVersion7();
        var replacementRuntimeId = Guid.CreateVersion7();
        var unrelatedRuntimeId = Guid.CreateVersion7();
        const long oldProcessingVersion = 7;
        const long replacementProcessingVersion = 3;
        const long unrelatedProcessingVersion = 5;
        const string providerReceiptJson = """{"id":"old-resource"}""";

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
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Runtime replacement cleanup",
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            Status = CompetitionStatus.Running,
            RunningSince = now.AddHours(-1),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Cleanup Team",
            NormalizedName = "CLEANUP TEAM",
            CaptainId = ownerId,
            MemberIds = [ownerId],
            InvitationToken = "0123456789abcdef0123456789abcdef",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/cleanup@sha256:"
                + new string('a', 64)),
            new RuntimeResourceLimits(67_108_864, 100_000_000, 64));
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Replacement target",
            DefinitionJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: runtime),
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
            RulesJson = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Ctf),
            UpdatedAt = now
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = oldRuntimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.Player,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "pool-a",
            RunnerId = "runner-a",
            State = RuntimeState.Stopping,
            ProcessingVersion = oldProcessingVersion,
            ProviderReceiptJson = providerReceiptJson,
            CreatedAt = now
        });

        if (includeReplacement)
        {
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = replacementRuntimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = competitionChallengeId,
                TeamId = teamId,
                Purpose = RuntimePurpose.Player,
                Generation = 2,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerPool = "pool-a",
                State = RuntimeState.Queued,
                ProcessingVersion = replacementProcessingVersion,
                ReplacesRuntimeInstanceId = oldRuntimeId,
                CreatedAt = now.AddSeconds(1)
            });
        }

        if (includeUnrelated)
        {
            var unrelatedChallengeId = Guid.CreateVersion7();
            var unrelatedCompetitionChallengeId = Guid.CreateVersion7();
            db.Challenges.Add(new Challenge
            {
                Id = unrelatedChallengeId,
                OwnerId = ownerId,
                Mode = GameMode.Ctf,
                Title = "Unrelated target",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = unrelatedCompetitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = unrelatedChallengeId,
                Order = 1,
                IsPublished = true,
                UpdatedAt = now
            });
            db.RuntimeInstances.Add(new RuntimeInstance
            {
                Id = unrelatedRuntimeId,
                CompetitionId = competitionId,
                CompetitionChallengeId = unrelatedCompetitionChallengeId,
                TeamId = teamId,
                Purpose = RuntimePurpose.Player,
                Generation = 1,
                RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker,
                RunnerPool = "pool-a",
                State = RuntimeState.Queued,
                ProcessingVersion = unrelatedProcessingVersion,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new Fixture(
            now,
            ownerId,
            competitionId,
            competitionChallengeId,
            teamId,
            oldRuntimeId,
            replacementRuntimeId,
            unrelatedRuntimeId,
            oldProcessingVersion,
            replacementProcessingVersion,
            unrelatedProcessingVersion,
            providerReceiptJson);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid UserId,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        Guid OldRuntimeId,
        Guid ReplacementRuntimeId,
        Guid UnrelatedRuntimeId,
        long OldProcessingVersion,
        long ReplacementProcessingVersion,
        long UnrelatedProcessingVersion,
        string ProviderReceiptJson);

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

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class RecordingCompetitionEventRecorder : ICompetitionEventRecorder
    {
        public List<CompetitionEventDraft> Drafts { get; } = [];

        public ValueTask<Guid> RecordAsync(
            CompetitionEventDraft draft,
            CancellationToken cancellationToken = default)
        {
            Drafts.Add(draft);
            return ValueTask.FromResult(Guid.CreateVersion7(draft.OccurredAt));
        }
    }
}

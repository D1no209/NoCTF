using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProviderHandler(
    IRuntimeProviderCatalog providers,
    IEnumerable<IRuntimeManagedResourceReconciler> resourceReconcilers,
    IConfiguration configuration,
    IRunnerCapacityGate capacity,
    IRuntimeNodeWorkReader workReader)
{
    public async Task<object> Handle(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    message.Definition.Provider,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        RuntimeFailureCode? failureCode = null;
        try
        {
            var receipt = await IsolatedContainerProvisioner.ProvisionAsync(
                providers.Containers(message.Definition.Provider),
                providers.Sandbox(message.Definition.Provider),
                message.Definition,
                DateTimeOffset.UtcNow,
                cancellationToken);
            ExpandedRuntimeUrls? expanded = null;
            try
            {
                expanded = RuntimeUrlExpander.ExpandContainer(
                    receipt,
                    message.Definition.UrlBindings,
                    message.Definition.ControlCheckUrlBinding,
                    message.Definition.AwdCheckerTargetBinding);
            }
            catch (InvalidOperationException)
            {
                failureCode = RuntimeFailureCode.UrlExpansionFailed;
            }
            if (expanded is not null)
                return new RuntimeProvisioned(
                    message.RuntimeInstanceId,
                    message.ProcessingVersion,
                    message.Generation,
                    ReadRunnerId(),
                    receipt.Provider,
                    JsonSerializer.Serialize(receipt),
                    expanded.Urls,
                    expanded.ParticipantUrlIndexes,
                    message.Definition.Ttl is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : null,
                    expanded.ControlCheckUrl,
                    expanded.AwdCheckerTargetHost);
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
        }
        return await CompleteProvisionFailureAsync(
            message,
            message.Definition.Provider,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> Handle(
        StopContainerRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        try
        {
            var work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
            var receipt = JsonSerializer.Deserialize<ContainerReceipt>(work.ProviderReceiptJson)
                ?? throw new InvalidOperationException("Provider receipt is invalid.");
            await IsolatedContainerProvisioner.DestroyAsync(
                providers.Containers(work.Provider),
                providers.Sandbox(work.Provider),
                receipt,
                cancellationToken);
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
        }
        catch (InvalidOperationException)
        {
            return new RuntimeStopFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.CleanupFailed);
        }
    }

    public async Task<object> Handle(
        ProvisionComposeRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    message.Definition.Provider,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        RuntimeFailureCode? failureCode = null;
        try
        {
            var runtime = providers.Compose(message.Definition.Provider);
            var receipt = await runtime.UpAsync(message.Definition, cancellationToken);
            var status = await runtime.GetStatusAsync(receipt, cancellationToken);
            if (status?.Status != RuntimeStatus.Running)
                failureCode = RuntimeFailureCode.ProviderRejected;
            else
            {
                ExpandedRuntimeUrls? expanded = null;
                try
                {
                    expanded = RuntimeUrlExpander.ExpandCompose(
                        receipt,
                        status,
                        message.Definition.UrlBindings,
                        message.Definition.ControlCheckUrlBinding,
                        message.Definition.AwdCheckerTargetBinding);
                }
                catch (InvalidOperationException)
                {
                    failureCode = RuntimeFailureCode.UrlExpansionFailed;
                }
                if (expanded is not null)
                    return new RuntimeProvisioned(
                        message.RuntimeInstanceId,
                        message.ProcessingVersion,
                        message.Generation,
                        ReadRunnerId(),
                        receipt.Provider,
                        JsonSerializer.Serialize(receipt),
                        expanded.Urls,
                        expanded.ParticipantUrlIndexes,
                        message.Definition.Ttl is { } ttl
                            ? DateTimeOffset.UtcNow.Add(ttl)
                            : null,
                        expanded.ControlCheckUrl,
                        expanded.AwdCheckerTargetHost);
            }
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
        }
        return await CompleteProvisionFailureAsync(
            message,
            message.Definition.Provider,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> Handle(
        StopComposeRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        try
        {
            var work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
            var receipt = JsonSerializer.Deserialize<ComposeReceipt>(work.ProviderReceiptJson)
                ?? throw new InvalidOperationException("Provider receipt is invalid.");
            await providers.Compose(work.Provider).DownAsync(receipt, cancellationToken);
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
        }
        catch (InvalidOperationException)
        {
            return new RuntimeStopFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.CleanupFailed);
        }
    }

    public async Task<object> Handle(
        ProvisionOvaRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        var workStatus = await workReader.ReadProvisionStatusAsync(message, cancellationToken);
        if (workStatus != RuntimeProvisionWorkStatus.Current)
        {
            if (workStatus == RuntimeProvisionWorkStatus.StopRequested)
                return await CancelProvisionAsync(
                    message,
                    RuntimeProvider.Libvirt,
                    cancellationToken);
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }

        RuntimeFailureCode? failureCode = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(message.Definition.OperationTimeout);
        try
        {
            var runtime = providers.Appliance(RuntimeProvider.Libvirt);
            var receipt = await runtime.ImportAsync(message.Definition, timeout.Token);
            ExpandedRuntimeUrls? expanded = null;
            try
            {
                expanded = RuntimeUrlExpander.ExpandOva(
                    receipt,
                    message.Definition.UrlBindings,
                    message.Definition.ControlCheckUrlBinding);
            }
            catch (InvalidOperationException)
            {
                failureCode = RuntimeFailureCode.UrlExpansionFailed;
            }
            if (expanded is not null)
                return new RuntimeProvisioned(
                    message.RuntimeInstanceId,
                    message.ProcessingVersion,
                    message.Generation,
                    ReadRunnerId(),
                    receipt.Provider,
                    JsonSerializer.Serialize(receipt),
                    expanded.Urls,
                    expanded.ParticipantUrlIndexes,
                    message.Definition.Ttl is { } ttl
                        ? DateTimeOffset.UtcNow.Add(ttl)
                        : null,
                    expanded.ControlCheckUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
        }
        catch (TimeoutException)
        {
            failureCode = RuntimeFailureCode.ProvisionTimeout;
        }
        catch (InvalidOperationException)
        {
            failureCode = RuntimeFailureCode.ProviderRejected;
        }
        return await CompleteProvisionFailureAsync(
            message,
            RuntimeProvider.Libvirt,
            failureCode ?? throw new InvalidOperationException("Runtime failure code is unavailable."),
            cancellationToken);
    }

    public async Task<object> Handle(
        StopOvaRuntime message,
        CancellationToken cancellationToken)
    {
        ValidateAssignment(message);
        try
        {
            var work = await workReader.ReadStopAsync(message, cancellationToken);
            if (work is null)
                return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
            if (work.Provider != RuntimeProvider.Libvirt)
                throw new InvalidOperationException("OVA Runtime receipt provider is invalid.");
            var receipt = JsonSerializer.Deserialize<OvaRuntimeReceipt>(work.ProviderReceiptJson)
                ?? throw new InvalidOperationException("Provider receipt is invalid.");
            await providers.Appliance(work.Provider).DestroyAsync(receipt, cancellationToken);
            await ReleaseStopCapacityOrThrowAsync(
                message.RuntimeInstanceId,
                cancellationToken);
            return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
        }
        catch (InvalidOperationException)
        {
            return new RuntimeStopFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.CleanupFailed);
        }
    }

    private void ValidateAssignment(IRunnerNodeMessage message)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        RunnerNodeAssignmentGuard.Validate(message, configuredPool, ReadRunnerId());
    }

    private string ReadRunnerId() => configuration["Runner:Id"]
        ?? throw new InvalidOperationException("Runner:Id is required.");

    private async Task<object> CompleteProvisionFailureAsync(
        IRuntimeProvisionMessage message,
        RuntimeProvider provider,
        RuntimeFailureCode failureCode,
        CancellationToken cancellationToken)
    {
        var reconciler = ReadResourceReconciler(provider);
        await reconciler.DestroyByIdentityAsync(
            new RuntimeResourceIdentity(message.RuntimeInstanceId, message.Generation),
            cancellationToken);
        await ReleaseCapacityOrThrowAsync(message, cancellationToken);
        return new RuntimeProvisionTerminated(
            message.RuntimeInstanceId,
            message.ProcessingVersion,
            message.Generation,
            message.RunnerPool,
            message.RunnerId,
            failureCode);
    }

    private async Task<RuntimeProvisionCanceled> CancelProvisionAsync(
        IRuntimeProvisionMessage message,
        RuntimeProvider provider,
        CancellationToken cancellationToken)
    {
        var reconciler = ReadResourceReconciler(provider);
        await reconciler.DestroyByIdentityAsync(
            new RuntimeResourceIdentity(message.RuntimeInstanceId, message.Generation),
            cancellationToken);
        await ReleaseCapacityOrThrowAsync(message, cancellationToken);
        return new RuntimeProvisionCanceled(
            message.RuntimeInstanceId,
            message.ProcessingVersion,
            message.Generation,
            message.RunnerPool,
            message.RunnerId);
    }

    private IRuntimeManagedResourceReconciler ReadResourceReconciler(
        RuntimeProvider provider) =>
        resourceReconcilers.SingleOrDefault(candidate => candidate.Provider == provider)
        ?? throw new InvalidOperationException(
            $"Runtime resource reconciliation is unavailable for '{provider}'.");

    private async Task ReleaseCapacityOrThrowAsync(
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken)
    {
        var release = await capacity.ReleaseAsync(
            message.RuntimeInstanceId,
            message.RunnerId,
            cancellationToken);
        if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            throw new InvalidOperationException(
                "Runtime capacity belongs to a different Runner assignment.");
    }

    private async Task ReleaseStopCapacityOrThrowAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken)
    {
        var release = await capacity.ReleaseAsync(
            runtimeInstanceId,
            ReadRunnerId(),
            cancellationToken);
        if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            throw new InvalidOperationException(
                "Runtime capacity belongs to a different Runner assignment.");
    }
}

public static class RuntimeWriteBackHandler
{
    public static async Task Handle(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null)
            return;
        if (IsLateProvisionSuccessAwaitingCleanup(instance, message))
        {
            instance.ProviderReceiptJson ??= message.ProviderReceiptJson;
            instance.RunnerAssignmentReleaseToken = null;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
            await PublishRuntimeStopAsync(outbox, instance, message.RunnerId);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }
        if (instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning
            || instance.Generation != message.Generation
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.RuntimeProvider != message.Provider)
            return;

        instance.RunnerId = message.RunnerId;
        instance.RunnerAssignmentReleaseToken = null;
        instance.ProviderReceiptJson = message.ProviderReceiptJson;
        instance.Urls = [.. message.Urls];
        instance.ParticipantUrlIndexes = [.. message.ParticipantUrlIndexes];
        instance.ControlCheckUrl = message.ControlCheckUrl;
        instance.AwdCheckerTargetHost = message.AwdCheckerTargetHost;
        instance.State = RuntimeState.Running;
        instance.RunningAt = DateTimeOffset.UtcNow;
        instance.ExpiresAt = message.ExpiresAt;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        if (message.AwdCheckerTargetHost is not null)
            instance.NextCheckerDueAt = instance.RunningAt;
        if (instance.Purpose == RuntimePurpose.AwdpTarget
            && instance.SubmissionId is Guid submissionId)
        {
            var submission = await db.Submissions
                .SingleOrDefaultAsync(item => item.Id == submissionId, cancellationToken);
            if (submission is null
                || submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing
                || submission.ProcessingVersion != instance.SubmissionProcessingVersion)
            {
                if (submission is not null
                    && submission.EvaluationState == NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing
                    && submission.ProcessingVersion == instance.SubmissionProcessingVersion)
                {
                    submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.PlatformFailed;
                    submission.EvaluationFailureCode = NoCTF.Domain.Submissions.ScoringFailureCode.CheckerPlatformError;
                    submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
                }
                instance.State = RuntimeState.Stopping;
                instance.RunnerAssignmentReleaseToken = null;
                instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
                await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    message.RunnerId));
                await RecordRuntimeStateAsync(
                    events,
                    instance,
                    CompetitionEventLevel.Warning,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                return;
            }
            if (submission?.PatchUploadId is Guid patchUploadId)
            {
                var deadline = instance.ExpiresAt
                    ?? DateTimeOffset.UtcNow.AddMinutes(15);
                await outbox.PublishToRunnerNodeAsync(new RunAwdpFixVerification(
                    submission.Id,
                    submission.CompetitionChallengeId,
                    patchUploadId,
                    instance.Id,
                    instance.Generation,
                    instance.SubmissionProcessingVersion.Value,
                    instance.ProcessingVersion,
                    deadline,
                    instance.RunnerPool,
                    message.RunnerId));
                await outbox.ScheduleAsync(new ExpireAwdpFixVerification(
                    submission.Id,
                    instance.Id,
                    instance.Generation,
                    instance.SubmissionProcessingVersion.Value,
                    instance.ProcessingVersion,
                    deadline,
                    instance.RunnerPool,
                    message.RunnerId), deadline);
            }
        }
        var now = DateTimeOffset.UtcNow;
        var currentAwdFlag = instance.TeamId is null
            ? null
            : await db.ChallengeFlags.AsNoTracking()
                .Where(flag => flag.CompetitionChallengeId == instance.CompetitionChallengeId
                    && flag.TeamId == instance.TeamId
                    && flag.SpecificationKind == NoCTF.Domain.Challenges.SpecificationKind.AwdRound
                    && flag.ValidStart <= now
                    && flag.ValidUntil > now
                    && flag.DeletedAt == null)
                .SingleOrDefaultAsync(cancellationToken);
        if (currentAwdFlag is not null)
        {
            await outbox.PublishToRunnerNodeAsync(new InjectAwdFlag(
                instance.Id,
                instance.CompetitionChallengeId,
                currentAwdFlag.Id,
                instance.Generation,
                instance.ProcessingVersion,
                currentAwdFlag.ValidUntil!.Value,
                instance.RunnerPool,
                instance.RunnerId));
        }
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.RunningAt ?? now,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        RuntimeProvisionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning
            || !string.Equals(
                instance.RunnerId,
                message.RunnerId,
                StringComparison.Ordinal))
            return;

        await PersistProvisionFailureAsync(
            instance,
            message.FailureCode,
            db,
            events,
            cancellationToken);
    }

    public static async Task Handle(
        RuntimeProvisionTerminated message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || message.ProvisionProcessingVersion == long.MaxValue
            || instance.Generation != message.Generation
            || !string.Equals(instance.RunnerPool, message.RunnerPool, StringComparison.Ordinal)
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.ProviderReceiptJson is not null)
            return;

        if (instance.State == RuntimeState.Provisioning
            && instance.ProcessingVersion == message.ProvisionProcessingVersion)
        {
            await PersistProvisionFailureAsync(
                instance,
                message.FailureCode,
                db,
                events,
                cancellationToken);
            return;
        }
        if (instance.State != RuntimeState.Stopping
            || instance.ProcessingVersion <= message.ProvisionProcessingVersion
            || instance.RunnerAssignmentReleaseToken is not null)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = DateTimeOffset.UtcNow;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                && candidate.State == RuntimeState.Queued,
            cancellationToken);
        if (replacement is not null)
            await outbox.PublishAsync(new DispatchRuntime(
                replacement.Id,
                replacement.ProcessingVersion));
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async Task PersistProvisionFailureAsync(
        RuntimeInstance instance,
        RuntimeFailureCode failureCode,
        NoCtfDbContext db,
        ICompetitionEventRecorder events,
        CancellationToken cancellationToken)
    {
        instance.State = RuntimeState.Failed;
        instance.FailureCode = failureCode;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        if (instance.Purpose == RuntimePurpose.AwdpTarget
            && instance.SubmissionId is Guid submissionId)
        {
            var submission = await db.Submissions.SingleOrDefaultAsync(
                item => item.Id == submissionId,
                cancellationToken);
            if (submission is not null
                && submission.EvaluationState == NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing
                && submission.ProcessingVersion == instance.SubmissionProcessingVersion)
            {
                submission.EvaluationState = NoCTF.Domain.Submissions.SubmissionEvaluationState.PlatformFailed;
                submission.EvaluationFailureCode = NoCTF.Domain.Submissions.ScoringFailureCode.CheckerPlatformError;
                submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Error,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeStopped message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Stopping)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = DateTimeOffset.UtcNow;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                && candidate.State == RuntimeState.Queued,
            cancellationToken);
        if (replacement is not null)
            await outbox.PublishAsync(new DispatchRuntime(replacement.Id, replacement.ProcessingVersion));
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Stopping)
            return;

        instance.State = RuntimeState.Failed;
        instance.FailureCode = message.FailureCode;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                && candidate.State == RuntimeState.Queued,
            cancellationToken);
        if (replacement is not null)
        {
            replacement.State = RuntimeState.Failed;
            replacement.FailureCode = message.FailureCode;
            replacement.ProcessingVersion = checked(replacement.ProcessingVersion + 1);
            await RecordRuntimeStateAsync(
                events,
                replacement,
                CompetitionEventLevel.Error,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Error,
            DateTimeOffset.UtcNow,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeProvisionCanceled message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || message.ProvisionProcessingVersion == long.MaxValue
            || instance.State != RuntimeState.Stopping
            || instance.ProcessingVersion <= message.ProvisionProcessingVersion
            || instance.Generation != message.Generation
            || !string.Equals(instance.RunnerPool, message.RunnerPool, StringComparison.Ordinal)
            || !string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal)
            || instance.RunnerAssignmentReleaseToken is not null
            || instance.ProviderReceiptJson is not null)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = DateTimeOffset.UtcNow;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        var replacement = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.ReplacesRuntimeInstanceId == instance.Id
                && candidate.State == RuntimeState.Queued,
            cancellationToken);
        if (replacement is not null)
            await outbox.PublishAsync(new DispatchRuntime(
                replacement.Id,
                replacement.ProcessingVersion));
        await RecordRuntimeStateAsync(
            events,
            instance,
            CompetitionEventLevel.Information,
            instance.StoppedAt.Value,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static ValueTask<Guid> RecordRuntimeStateAsync(
        ICompetitionEventRecorder events,
        RuntimeInstance instance,
        CompetitionEventLevel level,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        events.RecordAsync(new(
            instance.CompetitionId,
            CompetitionEventKind.RuntimeStateChanged,
            level,
            instance.TeamId is null
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Team,
            occurredAt,
            TeamId: instance.TeamId,
            CompetitionChallengeId: instance.CompetitionChallengeId,
            RuntimeInstanceId: instance.Id,
            SubmissionId: instance.SubmissionId,
            RuntimeState: instance.State,
            RuntimeGeneration: instance.Generation),
            cancellationToken);

    private static bool IsLateProvisionSuccessAwaitingCleanup(
        RuntimeInstance instance,
        RuntimeProvisioned message) =>
        message.ProcessingVersion < long.MaxValue
        && instance.State == RuntimeState.Stopping
        && instance.ProcessingVersion > message.ProcessingVersion
        && instance.Generation == message.Generation
        && instance.ProviderReceiptJson is null
        && instance.RunnerAssignmentReleaseToken is null
        && instance.RuntimeProvider == message.Provider
        && string.Equals(instance.RunnerId, message.RunnerId, StringComparison.Ordinal);

    private static ValueTask PublishRuntimeStopAsync(
        ITransactionalMessageOutbox outbox,
        RuntimeInstance instance,
        string runnerId) =>
        instance.RuntimeKind switch
        {
            RuntimeKind.Container => outbox.PublishToRunnerNodeAsync(
                new StopContainerRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            RuntimeKind.Compose => outbox.PublishToRunnerNodeAsync(
                new StopComposeRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            RuntimeKind.OvaVm => outbox.PublishToRunnerNodeAsync(
                new StopOvaRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    runnerId)),
            _ => throw new InvalidOperationException(
                $"Unsupported runtime kind '{instance.RuntimeKind}'.")
        };
}

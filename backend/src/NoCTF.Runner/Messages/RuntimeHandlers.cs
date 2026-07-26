using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.Messaging;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProviderHandler(
    IRuntimeProviderCatalog providers,
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
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
            {
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            }
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        try
        {
            var receipt = await IsolatedContainerProvisioner.ProvisionAsync(
                providers.Containers(message.Definition.Provider),
                providers.Sandbox(message.Definition.Provider),
                message.Definition,
                DateTimeOffset.UtcNow,
                cancellationToken);
            ExpandedRuntimeUrls expanded;
            try
            {
                expanded = RuntimeUrlExpander.ExpandContainer(
                    receipt,
                    message.Definition.UrlBindings,
                    message.Definition.ControlCheckUrlBinding);
            }
            catch (InvalidOperationException)
            {
                await IsolatedContainerProvisioner.DestroyAsync(
                    providers.Containers(message.Definition.Provider),
                    providers.Sandbox(message.Definition.Provider),
                    receipt,
                    cancellationToken);
                await ReleaseCapacityAsync(message, cancellationToken);
                return new RuntimeProvisionFailed(
                    message.RuntimeInstanceId,
                    message.ProcessingVersion,
                    RuntimeFailureCode.UrlExpansionFailed,
                    message.RunnerId);
            }
            return new RuntimeProvisioned(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                ReadRunnerId(),
                receipt.Provider,
                JsonSerializer.Serialize(receipt),
                expanded.Urls,
                expanded.ParticipantUrlIndexes,
                message.Definition.Ttl is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : null,
                expanded.ControlCheckUrl);
        }
        catch (TimeoutException)
        {
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProvisionTimeout,
                message.RunnerId);
        }
        catch (InvalidOperationException)
        {
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProviderRejected,
                message.RunnerId);
        }
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
            await capacity.ReleaseAsync(message.RuntimeInstanceId, ReadRunnerId(), cancellationToken);
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
            if (workStatus == RuntimeProvisionWorkStatus.AssignmentAbsent)
            {
                await capacity.ReleaseAsync(
                    message.RuntimeInstanceId,
                    message.RunnerId,
                    cancellationToken);
            }
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.RunnerUnavailable,
                message.RunnerId);
        }
        ComposeReceipt? receipt = null;
        try
        {
            var runtime = providers.Compose(message.Definition.Provider);
            receipt = await runtime.UpAsync(message.Definition, cancellationToken);
            var status = await runtime.GetStatusAsync(receipt, cancellationToken);
            if (status?.Status != RuntimeStatus.Running)
            {
                await runtime.DownAsync(receipt, cancellationToken);
                receipt = null;
                await ReleaseCapacityAsync(message, cancellationToken);
                return new RuntimeProvisionFailed(
                    message.RuntimeInstanceId,
                    message.ProcessingVersion,
                    RuntimeFailureCode.ProviderRejected,
                    message.RunnerId);
            }

            ExpandedRuntimeUrls expanded;
            try
            {
                expanded = RuntimeUrlExpander.ExpandCompose(
                    receipt,
                    status,
                    message.Definition.UrlBindings,
                    message.Definition.ControlCheckUrlBinding);
            }
            catch (InvalidOperationException)
            {
                await runtime.DownAsync(receipt, cancellationToken);
                receipt = null;
                await ReleaseCapacityAsync(message, cancellationToken);
                return new RuntimeProvisionFailed(
                    message.RuntimeInstanceId,
                    message.ProcessingVersion,
                    RuntimeFailureCode.UrlExpansionFailed,
                    message.RunnerId);
            }
            return new RuntimeProvisioned(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
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
        catch (TimeoutException)
        {
            if (receipt is not null)
                await providers.Compose(message.Definition.Provider)
                    .DownAsync(receipt, cancellationToken);
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProvisionTimeout,
                message.RunnerId);
        }
        catch (InvalidOperationException)
        {
            if (receipt is not null)
                await providers.Compose(message.Definition.Provider)
                    .DownAsync(receipt, cancellationToken);
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProviderRejected,
                message.RunnerId);
        }
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
            await capacity.ReleaseAsync(
                message.RuntimeInstanceId,
                ReadRunnerId(),
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

    private Task ReleaseCapacityAsync(
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken) =>
        capacity.ReleaseAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
}

public static class RuntimeWriteBackHandler
{
    public static async Task Handle(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning)
        {
            return;
        }

        instance.RunnerId = message.RunnerId;
        instance.ProviderReceiptJson = message.ProviderReceiptJson;
        instance.Urls = [.. message.Urls];
        instance.ParticipantUrlIndexes = [.. message.ParticipantUrlIndexes];
        instance.ControlCheckUrl = message.ControlCheckUrl;
        instance.State = RuntimeState.Running;
        instance.RunningAt = DateTimeOffset.UtcNow;
        instance.ExpiresAt = message.ExpiresAt;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        if (instance.Purpose == RuntimePurpose.AwdpTarget
            && instance.SubmissionId is Guid submissionId)
        {
            var submission = await db.Submissions
                .SingleOrDefaultAsync(item => item.Id == submissionId, cancellationToken);
            var currentRevisions = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.Id == instance.CompetitionChallengeId)
                .Join(
                    db.Competitions.AsNoTracking(),
                    challenge => challenge.CompetitionId,
                    competition => competition.Id,
                    (challenge, competition) => new
                    {
                        Challenge = (int?)challenge.Revision,
                        Competition = (int?)competition.ConfigurationRevision
                    })
                .SingleOrDefaultAsync(cancellationToken);
            if (submission is null
                || submission.EvaluationState != NoCTF.Domain.Submissions.SubmissionEvaluationState.Processing
                || submission.ProcessingVersion != instance.SubmissionProcessingVersion
                || currentRevisions?.Challenge != instance.ConfigurationRevision
                || currentRevisions?.Competition != instance.CompetitionConfigurationRevision)
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
                instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
                await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
                    instance.Id,
                    instance.ProcessingVersion,
                    instance.RunnerPool,
                    message.RunnerId));
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
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        RuntimeProvisionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning)
        {
            return;
        }

        instance.State = RuntimeState.Failed;
        instance.FailureCode = message.FailureCode;
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
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeStopped message,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
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
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    public static async Task Handle(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
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
        await db.SaveChangesAsync(cancellationToken);
    }
}

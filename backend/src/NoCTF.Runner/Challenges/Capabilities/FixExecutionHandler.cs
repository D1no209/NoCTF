using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Runner.Messages;

namespace NoCTF.Runner.Challenges.Capabilities;

public sealed record FixExecutionWork(
    FixArchive Archive,
    ContainerReceipt TargetReceipt,
    FixCapabilityDefinition Fix,
    CheckExecutionInput Checker);

public interface IFixExecutionWorkReader
{
    Task<FixExecutionWorkClaim> ClaimAsync(
        RunFixExecution message,
        CancellationToken cancellationToken);
}

public sealed record FixExecutionRecoveryWork(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    RuntimeReceiptData? ProviderReceipt,
    string RunnerId);

public sealed record FixExecutionWorkClaim(
    FixExecutionFenceDisposition Disposition,
    FixExecutionWork? Work = null,
    FixExecutionRecoveryWork? Recovery = null);

public enum FixExecutionStage
{
    ArchiveDownload,
    ArchivePreparation,
    TargetInputPreparation,
    TargetPatchExecution,
    CheckerInputPreparation,
    CheckerExecution,
    ResourceCleanup
}

public enum FixPlatformFailureCode
{
    ExecutionCanceled,
    ArchiveRejected,
    ArchivePreparationFailed,
    TargetInputInjectionFailed,
    TargetPatchExecutionFailed,
    CheckerInputInjectionFailed,
    CheckerStartFailed,
    ResourceCleanupFailed,
    UnexpectedPlatformFailure
}

public sealed class FixPlatformException(
    FixExecutionStage stage,
    FixPlatformFailureCode failureCode,
    RuntimeProvider provider,
    Exception innerException)
    : Exception("Fix platform execution failed.", innerException)
{
    public FixExecutionStage Stage { get; } = stage;
    public FixPlatformFailureCode FailureCode { get; } = failureCode;
    public RuntimeProvider Provider { get; } = provider;

    public override string ToString() =>
        $"FixPlatformException {{ Stage = {Stage}, FailureCode = {FailureCode}, "
        + $"Provider = {Provider}, Details = [REDACTED] }}";
}

public sealed class FixExecutionWorkReader(
    IDbContextFactory<NoCtfDbContext> contexts,
    IFixExecutionFence fence,
    IRunnerScoringTokenIssuer tokens,
    IOptions<RunnerScoringOptions> scoringOptions,
    TimeProvider timeProvider) : IFixExecutionWorkReader
{
    public async Task<FixExecutionWorkClaim> ClaimAsync(
        RunFixExecution message,
        CancellationToken cancellationToken)
    {
        var fenceResult = await fence.AcquireAsync(new(
            message.GameplayFactId,
            message.CompetitionChallengeId,
            message.PatchUploadId,
            message.RuntimeInstanceId,
            message.Deadline,
            message.RunnerId), cancellationToken);
        if (fenceResult.Disposition == FixExecutionFenceDisposition.Superseded)
            return new(FixExecutionFenceDisposition.Superseded);
        if (fenceResult.Disposition == FixExecutionFenceDisposition.Recover)
        {
            return new(
                FixExecutionFenceDisposition.Recover,
                Recovery: new(
                    message.GameplayFactId,
                    fenceResult.RuntimeInstanceId,
                    fenceResult.Provider,
                    fenceResult.ProviderReceipt,
                    fenceResult.RunnerId));
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await db.GameplayFacts.AsNoTracking()
            .Where(submission => submission.Id == message.GameplayFactId
                && submission.CompetitionChallengeId == message.CompetitionChallengeId
                && submission.Kind == GameplayFactKind.FixAttempt
                && submission.State == GameplayFactState.Processing
                && submission.ReferenceKind == GameplayFactReferenceKind.PatchUpload
                && submission.ReferenceId == message.PatchUploadId)
            .Join(
                db.PatchUploads.AsNoTracking(),
                submission => submission.ReferenceId,
                upload => upload.Id,
                (submission, upload) => new { GameplayFact = submission, Upload = upload })
            .Join(
                db.Files.AsNoTracking(),
                item => item.Upload.FileId,
                file => file.Id,
                (item, file) => new { item.GameplayFact, item.Upload, File = file })
            .Join(
                db.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.Id == message.RuntimeInstanceId
                        && (runtime.Purpose == RuntimePurpose.FixTarget
                            || runtime.Purpose == RuntimePurpose.ScopedFixTarget)
                        && runtime.GameplayFactId == message.GameplayFactId
                        && runtime.State == RuntimeState.Running
                        && runtime.RunnerId == message.RunnerId),
                pair => pair.GameplayFact.Id,
                runtime => runtime.GameplayFactId,
                (pair, runtime) => new
                {
                    pair.GameplayFact,
                    pair.Upload,
                    pair.File,
                    Runtime = runtime
                })
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                item => item.GameplayFact.CompetitionChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.GameplayFact,
                    item.Upload,
                    item.File,
                    item.Runtime,
                    challenge.ChallengeId
                })
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.Upload,
                    item.File,
                    item.Runtime,
                    ChallengeDefinition = challenge.Definition!,
                    item.GameplayFact
                })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || timeProvider.GetUtcNow() >= message.Deadline
            || target.Runtime.ProviderReceipt is null)
            return new(FixExecutionFenceDisposition.Superseded);

        var receipt = (target.Runtime.ProviderReceipt?.ToData()
            as ContainerRuntimeReceiptData);
        if (receipt is null || target.ChallengeDefinition.Fix is not { } fix || target.ChallengeDefinition.Check is not { } check
            || receipt.Services.SingleOrDefault(service => service.Name == check.TargetServiceName)?.InternalHost is not { Length: > 0 } targetHost
            || !receipt.Services.Any(service => service.Name == fix.TargetServiceName)) return new(FixExecutionFenceDisposition.Superseded);
        var targetReceipt = receipt.ServiceReceipt(fix.TargetServiceName);
        var checkerReceipt = receipt.ServiceReceipt(check.TargetServiceName);
        var baseUri = scoringOptions.Value.CallbackBaseUrl
            ?? throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        var now = timeProvider.GetUtcNow();
        var remaining = message.Deadline - now;
        if (remaining <= TimeSpan.Zero)
            return new(FixExecutionFenceDisposition.Superseded);
        var checkerTimeout = TimeSpan.FromSeconds(check.TimeoutSeconds);
        var timeout = checkerTimeout < remaining ? checkerTimeout : remaining;
        var callbackTokenRequest = new FixCheckResultTokenRequest(
            message.RunnerId,
            message.GameplayFactId,
            message.RuntimeInstanceId,
            message.Deadline,
            now);
        var callbackToken = tokens.IssueFixCheckResult(callbackTokenRequest);
        var archiveToken = tokens.IssueFixArchiveRead(
            message.RunnerId,
            message.PatchUploadId,
            message.GameplayFactId,
            now);
        return new(
            FixExecutionFenceDisposition.Execute,
            new(
                new(
                    new Uri(
                        baseUri,
                        $"/api/internal/v1/fixes/{message.GameplayFactId:D}/archive"),
                    archiveToken,
                    target.File.FileName,
                    target.File.ByteLength,
                    target.File.Sha256),
                targetReceipt,
                fix,
                new(
                    message.RuntimeInstanceId,
                    target.Runtime.RuntimeProvider,
                    check.Image,
                    check.Command.OrderBy(argument => argument.Position).Select(argument => argument.Value).ToArray(),
                    check.Environment.ToDictionary(variable => variable.Name, variable => variable.Value),
                    checkerReceipt,
                    targetHost,
                    check.ReadyTimeoutSeconds,
                    new Uri(baseUri, "/api/internal/v1/capabilities/check-results"),
                    callbackToken,
                    timeout,
                    check.AcceptsFixInput,
                    message.GameplayFactId)
                {
                    Requirements = check.Requirements.Where(item => item.Kind is CheckItemKind.Connection or CheckItemKind.Functionality or CheckItemKind.Vulnerability)
                        .Select(item => item.Kind).Append(CheckItemKind.Functionality).Distinct().ToArray()
                }));
    }
}

[NonTransactional]
public sealed class FixExecutionHandler(
    IFixExecutionWorkReader reader,
    FixArchiveDownloader archives,
    FixArchivePreparer preparer,
    IOptions<FixVerificationOptions> verificationOptions,
    IRuntimeProviderCatalog providers,
    ExecuteFix fix,
    IFixExecutionProgressStore progress,
    ExecuteCheck checker,
    IEnumerable<IRuntimeManagedResourceReconciler> resourceReconcilers,
    IRunnerCapacityGate capacity,
    IPostCommitMessagePublisher outbox,
    IOptions<RunnerOptions> runnerOptions,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider,
    ILogger<FixExecutionHandler> logger,
    NoCTF.Infrastructure.Runtime.Capacity.RunnerResourceMutationCoordinator? mutations = null)
{
    public async Task Handle(
        RunFixExecution message,
        CancellationToken cancellationToken)
    {
        using var targetMutation = mutations is null ? null : await mutations.EnterRuntimeAsync(message.RuntimeInstanceId, cancellationToken);
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        FixWorkCompletion? outcome;
        try
        {
            var remaining = message.Deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                outcome = new(FixWorkCompletionKind.PlatformFailed, CapabilityExecutionFailure.DeadlineExceeded, FixExecutionPhase.Queued);
            else
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                deadline.CancelAfter(remaining);
                outcome = await ExecuteAsync(message, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (
            applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError(
                "Fix execution was canceled outside host shutdown. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                FixPlatformFailureCode.ExecutionCanceled,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = new(FixWorkCompletionKind.PlatformFailed, CapabilityExecutionFailure.ExecutionInterrupted);
        }
        catch (InvalidDataException)
        {
            logger.LogWarning(
                "Fix archive or entrypoint is invalid. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                FixPlatformFailureCode.ArchiveRejected,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = new(FixWorkCompletionKind.Rejected, FailedPhase: FixExecutionPhase.Applying);
        }
        catch (FixPlatformException exception)
        {
            logger.LogError(
                "Fix platform stage failed. Stage={Stage} FailureCode={FailureCode} "
                + "Provider={Provider} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                exception.Stage,
                exception.FailureCode,
                exception.Provider,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = new(FixWorkCompletionKind.PlatformFailed,
                exception.Stage == FixExecutionStage.ResourceCleanup ? CapabilityExecutionFailure.ResourceCleanupFailed
                    : exception.Stage is FixExecutionStage.ArchiveDownload or FixExecutionStage.ArchivePreparation or FixExecutionStage.TargetInputPreparation
                        ? CapabilityExecutionFailure.InputPreparationFailed : CapabilityExecutionFailure.TargetExecutionFailed,
                exception.Stage == FixExecutionStage.ResourceCleanup ? FixExecutionPhase.Finalizing
                    : exception.Stage is FixExecutionStage.CheckerExecution or FixExecutionStage.CheckerInputPreparation
                        ? FixExecutionPhase.Checking : FixExecutionPhase.Applying);
        }
        catch (Exception)
        {
            logger.LogError(
                "Fix execution failed unexpectedly. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                FixPlatformFailureCode.UnexpectedPlatformFailure,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = new(FixWorkCompletionKind.PlatformFailed, CapabilityExecutionFailure.ExecutionInterrupted);
        }

        if (outcome is { } result)
            await PublishResultWithCompensationAsync(message, result);
    }

    private async Task<FixWorkCompletion?> ExecuteAsync(
        RunFixExecution message,
        CancellationToken cancellationToken)
    {
        var claim = await reader.ClaimAsync(message, cancellationToken);
        if (claim.Disposition == FixExecutionFenceDisposition.Recover)
        {
            await RecoverAsync(
                claim.Recovery
                    ?? throw new InvalidOperationException("Fix recovery work is unavailable."),
                cancellationToken);
            return null;
        }
        var work = claim.Work;
        if (claim.Disposition != FixExecutionFenceDisposition.Execute
            || work is null)
            return null;

        FixWorkCompletion outcome = new(FixWorkCompletionKind.Completed);
        var operationDirectory = OperationDirectory(message.GameplayFactId, message.RuntimeInstanceId);
        var workDirectory = Path.Combine(operationDirectory, "work");
        var archivePath = Path.Combine(operationDirectory, "fix.tar.gz");
        var tarPath = Path.Combine(operationDirectory, "fix.tar");
        try
        {
            var download = await archives.DownloadAsync(
                work.Archive,
                archivePath,
                cancellationToken);
            if (download != FixArchiveDownloadOutcome.Downloaded)
            {
                logger.LogWarning(
                    "Fix archive failed before patch execution. "
                    + "Stage={Stage} FailureCode={FailureCode} Provider={Provider} "
                    + "GameplayFactId={GameplayFactId} RuntimeInstanceId={RuntimeInstanceId} "
                    + "PatchUploadId={PatchUploadId}",
                    FixExecutionStage.ArchiveDownload,
                    download,
                    work.TargetReceipt.Provider,
                    message.GameplayFactId,
                    message.RuntimeInstanceId,
                    message.PatchUploadId);
                outcome = new(FixWorkCompletionKind.PlatformFailed, CapabilityExecutionFailure.InputPreparationFailed, FixExecutionPhase.Applying);
            }
            else
            {
                try
                {
                    await using var source = File.OpenRead(archivePath);
                    await preparer.PrepareTarAsync(
                        source,
                        work.Archive.OriginalFileName,
                        work.Fix.Entrypoint,
                        workDirectory,
                        tarPath,
                        cancellationToken);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new FixPlatformException(
                        FixExecutionStage.ArchivePreparation,
                        FixPlatformFailureCode.ArchivePreparationFailed,
                        work.TargetReceipt.Provider,
                        exception);
                }
                await using var targetTar = File.OpenRead(tarPath);
                var executionResult = await fix.ExecuteAsync(new(message.GameplayFactId, message.RuntimeInstanceId, work.Fix,
                    work.TargetReceipt, targetTar, message.Deadline), cancellationToken);
                if (!executionResult.Succeeded) return new(FixWorkCompletionKind.PlatformFailed, executionResult.FailureCode, FixExecutionPhase.Applying);
                var patch = executionResult.Value!;
                if (!await progress.RecordAppliedAsync(patch.ToGameplayOutcome(), cancellationToken)) return null;
                if (patch.ExecutionState == CapabilityExecutionState.PlatformFailed)
                    return new(FixWorkCompletionKind.PlatformFailed, patch.ExecutionFailure, FixExecutionPhase.Applying);
                if (patch.Result == FixResult.TimedOut)
                {
                    logger.LogWarning(
                        "Target Patch did not complete. Stage={Stage} FailureCode={FailureCode} "
                        + "Provider={Provider} GameplayFactId={GameplayFactId} "
                        + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                        FixExecutionStage.TargetPatchExecution,
                        FixResult.TimedOut,
                        work.TargetReceipt.Provider,
                        message.GameplayFactId,
                        message.RuntimeInstanceId,
                        message.PatchUploadId);
                    outcome = new(FixWorkCompletionKind.Completed);
                }
                else if (patch.Result != FixResult.Applied)
                {
                    logger.LogWarning(
                        "Target Patch did not complete. Stage={Stage} FailureCode={FailureCode} "
                        + "Provider={Provider} GameplayFactId={GameplayFactId} "
                        + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                        FixExecutionStage.TargetPatchExecution,
                        FixResult.Failed,
                        work.TargetReceipt.Provider,
                        message.GameplayFactId,
                        message.RuntimeInstanceId,
                        message.PatchUploadId);
                    outcome = new(FixWorkCompletionKind.Completed);
                }
                else
                {
                    NoCTF.Application.Common.OperationResult<CheckExecutionReceipt, CapabilityExecutionFailure> execution;
                    if (work.Checker.FixInputEnabled)
                    {
                        await using var checkerTar = File.OpenRead(tarPath);
                        execution = await checker.ExecuteAsync(
                            work.Checker,
                            new(
                                checkerTar,
                                OneShotInputArchive.RootDestinationPath),
                            cancellationToken);
                    }
                    else
                    {
                        execution = await checker.ExecuteAsync(
                            work.Checker,
                            null,
                            cancellationToken);
                    }
                    if (!execution.Succeeded)
                    {
                        logger.LogWarning(
                            "Checker did not complete normally. Stage={Stage} "
                            + "FailureCode={FailureCode} Provider={Provider} "
                            + "GameplayFactId={GameplayFactId} RuntimeInstanceId={RuntimeInstanceId} "
                            + "PatchUploadId={PatchUploadId}",
                            FixExecutionStage.CheckerExecution,
                            execution.FailureCode,
                            work.Checker.Provider,
                            message.GameplayFactId,
                            message.RuntimeInstanceId,
                            message.PatchUploadId);
                    }
                    outcome = execution.Succeeded ? new(FixWorkCompletionKind.Completed)
                        : new(FixWorkCompletionKind.PlatformFailed, execution.FailureCode,
                            execution.FailureCode == CapabilityExecutionFailure.ResourceCleanupFailed ? FixExecutionPhase.Finalizing : FixExecutionPhase.Checking);
                }
            }
        }
        finally
        {
            TryDeleteDirectory(operationDirectory);
        }

        return outcome;
    }

    private async Task PublishResultWithCompensationAsync(
        RunFixExecution message,
        FixWorkCompletion outcome)
    {
        using var compensation = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetime.ApplicationStopping);
        compensation.CancelAfter(TimeSpan.FromSeconds(
            FixExecutionBudget.ResultPublicationBudgetSeconds));
        await outbox.PublishAsync(new CompleteFixExecutionWork(
            message.GameplayFactId,
            message.RuntimeInstanceId,
            outcome,
            timeProvider.GetUtcNow())).AsTask().WaitAsync(compensation.Token);
        await outbox.SaveChangesAndFlushAsync(compensation.Token).WaitAsync(compensation.Token);
    }

    private async Task RecoverAsync(
        FixExecutionRecoveryWork recovery,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterAsync(cancellationToken);
        var identity = new RuntimeResourceIdentity(recovery.RuntimeInstanceId);
        if (recovery.ProviderReceipt is not null)
        {
            await RuntimeReceiptCleanup.CleanupContainerAsync(
                providers,
                identity,
                recovery.Provider,
                recovery.ProviderReceipt,
                cancellationToken);
        }
        else
        {
            var reconciler = resourceReconcilers.SingleOrDefault(
                candidate => candidate.Provider == recovery.Provider)
                ?? throw new InvalidOperationException(
                    $"Runtime resource reconciliation is unavailable for '{recovery.Provider}'.");
            await reconciler.DestroyByIdentityAsync(identity, cancellationToken);
            var remaining = await reconciler.ListManagedAsync(cancellationToken);
            if (remaining.Contains(identity))
                throw new InvalidOperationException(
                    "Fix verification resources remain after identity-based cleanup.");
        }

        TryDeleteOperationDirectory(recovery.GameplayFactId, recovery.RuntimeInstanceId);
        var release = await capacity.ReleaseAsync(
            recovery.RuntimeInstanceId,
            recovery.RunnerId,
            cancellationToken);
        if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
            throw new InvalidOperationException(
                "Fix target capacity belongs to a different Runner assignment.");

        await outbox.PublishAsync(new RecoverFixExecution(
            recovery.GameplayFactId,
            recovery.RuntimeInstanceId,
            recovery.RunnerId,
            timeProvider.GetUtcNow()));
        await outbox.FlushOutgoingMessagesAsync();
    }

    private void TryDeleteOperationDirectory(
        Guid gameplayFactId,
        Guid runtimeInstanceId)
    {
        var path = OperationDirectory(gameplayFactId, runtimeInstanceId);
        TryDeleteDirectory(path);
    }

    private string OperationDirectory(Guid gameplayFactId, Guid runtimeInstanceId) => Path.GetFullPath(Path.Combine(
        verificationOptions.Value.WorkDirectory, $"{gameplayFactId:N}-{runtimeInstanceId:N}"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Local scratch cleanup must not prevent the durable result or resource cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Local scratch cleanup must not prevent the durable result or resource cleanup.
        }
    }
}

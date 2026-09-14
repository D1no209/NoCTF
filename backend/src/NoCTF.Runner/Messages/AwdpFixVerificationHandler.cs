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
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.PatchVerification.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public sealed record AwdpCheckerWork(
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    string NetworkId,
    string TargetHost,
    int TargetReadyTimeoutSeconds,
    Uri CallbackUrl,
    string CallbackToken,
    TimeSpan Timeout,
    bool FixInputEnabled = false,
    bool AllowRoot = false)
{
    public override string ToString() =>
        $"AwdpCheckerWork {{ RuntimeInstanceId = {RuntimeInstanceId}, Provider = {Provider}, "
        + $"Image = {Image}, CommandCount = {Command.Count}, EnvironmentCount = {Environment.Count}, "
        + $"NetworkId = [REDACTED], TargetHost = [REDACTED], "
        + $"TargetReadyTimeoutSeconds = {TargetReadyTimeoutSeconds}, CallbackUrl = [REDACTED], "
        + $"CallbackToken = [REDACTED], Timeout = {Timeout}, FixInputEnabled = {FixInputEnabled} }}";
}

public sealed record AwdpFixWork(
    AwdpFixArchive Archive,
    ContainerReceipt TargetReceipt,
    string PatchEntrypoint,
    IReadOnlyList<string> PatchCommand,
    TimeSpan PatchTimeout,
    AwdpCheckerWork Checker);

public interface IAwdpFixWorkReader
{
    Task<AwdpFixWorkClaim> ClaimAsync(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken);
}

public sealed record AwdpFixRecoveryWork(
    Guid GameplayFactId,
    Guid RuntimeInstanceId,
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
    string RunnerId);

public sealed record AwdpFixWorkClaim(
    AwdpFixExecutionFenceDisposition Disposition,
    AwdpFixWork? Work = null,
    AwdpFixRecoveryWork? Recovery = null);

public interface IAwdpCheckerExecutor
{
    Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
        CancellationToken cancellationToken);

    Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
        OneShotInputArchive input,
        CancellationToken cancellationToken);
}

public enum AwdpFixExecutionStage
{
    ArchiveDownload,
    ArchivePreparation,
    TargetInputPreparation,
    TargetPatchExecution,
    CheckerInputPreparation,
    CheckerExecution,
    ResourceCleanup
}

public enum AwdpFixPlatformFailureCode
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

public sealed class AwdpFixPlatformException(
    AwdpFixExecutionStage stage,
    AwdpFixPlatformFailureCode failureCode,
    RuntimeProvider provider,
    Exception innerException)
    : Exception("AWDP Fix platform execution failed.", innerException)
{
    public AwdpFixExecutionStage Stage { get; } = stage;
    public AwdpFixPlatformFailureCode FailureCode { get; } = failureCode;
    public RuntimeProvider Provider { get; } = provider;

    public override string ToString() =>
        $"AwdpFixPlatformException {{ Stage = {Stage}, FailureCode = {FailureCode}, "
        + $"Provider = {Provider}, Details = [REDACTED] }}";
}

public enum AwdpCheckerExecutionOutcome
{
    Completed,
    AbnormalExit,
    TimedOut
}

public static class AwdpCheckerCompletionPolicy
{
    public static AwdpFixOutcome? ResultFor(AwdpCheckerExecutionOutcome outcome) => outcome switch
    {
        AwdpCheckerExecutionOutcome.Completed => null,
        AwdpCheckerExecutionOutcome.AbnormalExit => AwdpFixOutcome.PlatformFailed,
        AwdpCheckerExecutionOutcome.TimedOut => AwdpFixOutcome.ServiceAbnormal,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}

public static class AwdpPatchCommand
{
    public static IReadOnlyList<string> Create(
        IReadOnlyList<string>? command,
        string patchEntrypoint)
    {
        var entrypoint = $"/noctf/fix/{patchEntrypoint}";
        if (command is not { Count: > 0 })
            return ["/bin/sh", entrypoint];
        if (command.Count > AwdpPatchCommandRules.MaximumArguments
            || command.Any(string.IsNullOrWhiteSpace)
            || command.Any(argument =>
                argument.Length > AwdpPatchCommandRules.MaximumArgumentLength)
            || command.Count(argument => string.Equals(
                argument,
                AwdpPatchCommandRules.EntrypointPlaceholder,
                StringComparison.Ordinal)) != 1)
        {
            throw new InvalidOperationException("AWDP PatchCommand is invalid.");
        }
        return [.. command.Select(argument =>
                string.Equals(
                    argument,
                    AwdpPatchCommandRules.EntrypointPlaceholder,
                    StringComparison.Ordinal)
                    ? entrypoint
                    : argument)];
    }
}

public sealed class AwdpFixWorkReader(
    IServiceScopeFactory scopes,
    IRunnerScoringTokenIssuer tokens,
    IOptions<RunnerScoringOptions> scoringOptions,
    TimeProvider timeProvider) : IAwdpFixWorkReader
{
    public async Task<AwdpFixWorkClaim> ClaimAsync(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var fence = scope.ServiceProvider.GetRequiredService<IAwdpFixExecutionFence>();
        var fenceResult = await fence.AcquireAsync(new(
            message.GameplayFactId,
            message.CompetitionChallengeId,
            message.PatchUploadId,
            message.RuntimeInstanceId,
            message.Deadline,
            message.RunnerId), cancellationToken);
        if (fenceResult.Disposition == AwdpFixExecutionFenceDisposition.Superseded)
            return new(AwdpFixExecutionFenceDisposition.Superseded);
        if (fenceResult.Disposition == AwdpFixExecutionFenceDisposition.Recover)
        {
            return new(
                AwdpFixExecutionFenceDisposition.Recover,
                Recovery: new(
                    message.GameplayFactId,
                    fenceResult.RuntimeInstanceId,
                    fenceResult.Provider,
                    fenceResult.ProviderReceiptJson,
                    fenceResult.RunnerId));
        }

        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
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
                        && (runtime.Purpose == RuntimePurpose.AwdpTarget
                            || runtime.Purpose == RuntimePurpose.PatchVerificationTarget)
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
                    ChallengeRulesJson = challenge.RulesJson,
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
                    item.ChallengeRulesJson,
                    ChallengeDefinitionJson = challenge.DefinitionJson,
                    item.GameplayFact
                })
            .Join(
                db.Competitions.AsNoTracking(),
                item => item.GameplayFact.CompetitionId,
                competition => competition.Id,
                (item, competition) => new
                {
                    item.Upload,
                    item.File,
                    item.Runtime,
                    item.ChallengeRulesJson,
                    item.ChallengeDefinitionJson,
                    CompetitionConfigurationJson = competition.ConfigurationJson,
                    competition.Mode
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || timeProvider.GetUtcNow() >= message.Deadline
            || string.IsNullOrWhiteSpace(target.Runtime.ProviderReceiptJson))
            return new(AwdpFixExecutionFenceDisposition.Superseded);

        var settings = PatchVerificationConfigurationResolver.Resolve(
            target.Mode,
            target.CompetitionConfigurationJson,
            target.ChallengeRulesJson,
            target.ChallengeDefinitionJson);
        if (settings is null)
            return new(AwdpFixExecutionFenceDisposition.Superseded);
        var checker = settings.Checker;
        var receipt = JsonSerializer.Deserialize<ContainerReceipt>(
            target.Runtime.ProviderReceiptJson);
        if (receipt?.NetworkId is not { Length: > 0 } networkId
            || receipt.InternalHost is not { Length: > 0 } targetHost)
            return new(AwdpFixExecutionFenceDisposition.Superseded);

        var baseUri = scoringOptions.Value.CallbackBaseUrl
            ?? throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        var now = timeProvider.GetUtcNow();
        var remaining = message.Deadline - now;
        if (remaining <= TimeSpan.Zero)
            return new(AwdpFixExecutionFenceDisposition.Superseded);
        var checkerTimeout = TimeSpan.FromSeconds(checker.TimeoutSeconds);
        var timeout = checkerTimeout < remaining ? checkerTimeout : remaining;
        var callbackTokenRequest = new AwdpFixResultTokenRequest(
            message.RunnerId,
            message.GameplayFactId,
            message.RuntimeInstanceId,
            message.Deadline,
            now);
        var callbackToken = target.Mode == GameMode.Ctf
            ? tokens.IssuePatchVerificationResult(callbackTokenRequest)
            : tokens.IssueAwdpFixResult(callbackTokenRequest);
        var archiveToken = tokens.IssueFixArchiveRead(
            message.RunnerId,
            message.PatchUploadId,
            message.GameplayFactId,
            now);
        var patchCommand = AwdpPatchCommand.Create(
            settings.PatchCommand,
            settings.PatchEntrypoint);
        return new(
            AwdpFixExecutionFenceDisposition.Execute,
            new(
                new(
                    new Uri(
                        baseUri,
                        $"/api/internal/v1/awdp/fix-archives/{message.GameplayFactId:D}"),
                    archiveToken,
                    target.File.FileName,
                    target.File.ByteLength,
                    target.File.Sha256),
                receipt,
                settings.PatchEntrypoint,
                patchCommand,
                TimeSpan.FromSeconds(settings.PatchTimeoutSeconds),
                new(
                    message.RuntimeInstanceId,
                    target.Runtime.RuntimeProvider,
                    checker.Image,
                    checker.Command ?? [],
                    checker.Environment ?? new Dictionary<string, string>(),
                    networkId,
                    targetHost,
                    settings.ReadyTimeoutSeconds,
                    new Uri(baseUri, target.Mode == GameMode.Ctf
                        ? "/api/internal/v1/patch-verification/results"
                        : "/api/internal/v1/awdp/fix-results"),
                    callbackToken,
                    timeout,
                    settings.CheckerFixInput,
                    settings.CheckerAllowRoot)));
    }
}

public sealed class AwdpCheckerExecutor(IOneShotRuntimeProviderCatalog providers)
    : IAwdpCheckerExecutor
{
    public Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(work, input: null, cancellationToken);

    public Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
        OneShotInputArchive input,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(work, input, cancellationToken);

    private async Task<AwdpCheckerExecutionOutcome> ExecuteCoreAsync(
        AwdpCheckerWork work,
        OneShotInputArchive? input,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(work.Environment, StringComparer.Ordinal)
        {
            ["TARGET_HOST"] = work.TargetHost,
            ["TARGET_READY_TIMEOUT_SECONDS"] = work.TargetReadyTimeoutSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["NOCTF_CALLBACK_URL"] = work.CallbackUrl.AbsoluteUri,
            ["NOCTF_CALLBACK_TOKEN"] = work.CallbackToken
        };
        var request = new ContainerRequest(
            CreateOperationId(work.RuntimeInstanceId),
            work.Provider,
            work.Image,
            work.Command,
            environment,
            new Dictionary<string, string>(),
            new Dictionary<int, int>(),
            new RuntimeResourceLimits(256 * 1024 * 1024, 250_000_000, 128),
            new ContainerSecurityPolicy(true, input is null, !work.AllowRoot, ["ALL"], []),
            work.Timeout,
            NetworkName: work.NetworkId,
            OperationTimeout: work.Timeout,
            AllowInternalCallback: true,
            RuntimeInstanceId: work.RuntimeInstanceId,
            NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(work.Timeout);
        try
        {
            var runner = providers.OneShot(work.Provider);
            var result = input is null
                ? await runner.RunAsync(request, timeout.Token)
                : await runner.RunAsync(request, input, timeout.Token);
            return result.ExitCode == 0
                ? AwdpCheckerExecutionOutcome.Completed
                : AwdpCheckerExecutionOutcome.AbnormalExit;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            if (input is not null && !input.PreparationCompleted)
            {
                throw new AwdpFixPlatformException(
                    AwdpFixExecutionStage.CheckerInputPreparation,
                    AwdpFixPlatformFailureCode.CheckerInputInjectionFailed,
                    work.Provider,
                    new TimeoutException("Checker input preparation exceeded its execution budget."));
            }
            return AwdpCheckerExecutionOutcome.TimedOut;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OneShotInputPreparationException exception)
        {
            throw new AwdpFixPlatformException(
                AwdpFixExecutionStage.CheckerInputPreparation,
                AwdpFixPlatformFailureCode.CheckerInputInjectionFailed,
                work.Provider,
                exception);
        }
        catch (OneShotCleanupException exception)
        {
            throw new AwdpFixPlatformException(
                AwdpFixExecutionStage.ResourceCleanup,
                AwdpFixPlatformFailureCode.ResourceCleanupFailed,
                work.Provider,
                exception);
        }
        catch (Exception exception)
        {
            throw new AwdpFixPlatformException(
                AwdpFixExecutionStage.CheckerExecution,
                AwdpFixPlatformFailureCode.CheckerStartFailed,
                work.Provider,
                exception);
        }
    }

    private static Guid CreateOperationId(Guid runtimeInstanceId)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeInstanceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input[16..], 0x617764702d63686b);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}

[NonTransactional]
public sealed class AwdpFixVerificationHandler(
    IAwdpFixWorkReader reader,
    AwdpFixArchiveDownloader archives,
    FixArchivePreparer preparer,
    IRuntimeProviderCatalog providers,
    IAwdpCheckerExecutor checker,
    IEnumerable<IRuntimeManagedResourceReconciler> resourceReconcilers,
    IRunnerCapacityGate capacity,
    ITransactionalMessageOutbox outbox,
    IOptions<RunnerOptions> runnerOptions,
    IHostApplicationLifetime applicationLifetime,
    TimeProvider timeProvider,
    ILogger<AwdpFixVerificationHandler> logger)
{
    public Task Handle(
        RunPatchVerification message,
        CancellationToken cancellationToken) =>
        Handle(new RunAwdpFixVerification(
            message.GameplayFactId,
            message.CompetitionChallengeId,
            message.PatchUploadId,
            message.RuntimeInstanceId,
            message.Deadline,
            message.RunnerId), cancellationToken);

    public async Task Handle(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        AwdpFixOutcome? outcome;
        try
        {
            var remaining = message.Deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                outcome = AwdpFixOutcome.PlatformFailed;
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
                "AWDP Fix execution was canceled outside host shutdown. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                AwdpFixPlatformFailureCode.ExecutionCanceled,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = AwdpFixOutcome.PlatformFailed;
        }
        catch (InvalidDataException)
        {
            logger.LogWarning(
                "AWDP Fix archive or entrypoint is invalid. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                AwdpFixPlatformFailureCode.ArchiveRejected,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = AwdpFixOutcome.PatchFailed;
        }
        catch (AwdpFixPlatformException exception)
        {
            logger.LogError(
                "AWDP Fix platform stage failed. Stage={Stage} FailureCode={FailureCode} "
                + "Provider={Provider} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                exception.Stage,
                exception.FailureCode,
                exception.Provider,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = AwdpFixOutcome.PlatformFailed;
        }
        catch (Exception)
        {
            logger.LogError(
                "AWDP Fix execution failed unexpectedly. "
                + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                AwdpFixPlatformFailureCode.UnexpectedPlatformFailure,
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.PatchUploadId);
            outcome = AwdpFixOutcome.PlatformFailed;
        }

        if (outcome is AwdpFixOutcome result)
            await PublishResultWithCompensationAsync(message, result);
    }

    private async Task<AwdpFixOutcome?> ExecuteAsync(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        var claim = await reader.ClaimAsync(message, cancellationToken);
        if (claim.Disposition == AwdpFixExecutionFenceDisposition.Recover)
        {
            await RecoverAsync(
                claim.Recovery
                    ?? throw new InvalidOperationException("AWDP recovery work is unavailable."),
                cancellationToken);
            return null;
        }
        var work = claim.Work;
        if (claim.Disposition != AwdpFixExecutionFenceDisposition.Execute
            || work is null)
            return null;

        AwdpFixOutcome? outcome = AwdpFixOutcome.PlatformFailed;
        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "noctf-awdp",
            $"{message.GameplayFactId:N}-{message.RuntimeInstanceId:N}");
        var workDirectory = Path.Combine(operationDirectory, "work");
        var archivePath = Path.Combine(operationDirectory, "fix.tar.gz");
        var tarPath = Path.Combine(operationDirectory, "fix.tar");
        try
        {
            var download = await archives.DownloadAsync(
                work.Archive,
                archivePath,
                cancellationToken);
            if (download != AwdpFixArchiveDownloadOutcome.Downloaded)
            {
                logger.LogWarning(
                    "AWDP Fix archive failed before patch execution. "
                    + "Stage={Stage} FailureCode={FailureCode} Provider={Provider} "
                    + "GameplayFactId={GameplayFactId} RuntimeInstanceId={RuntimeInstanceId} "
                    + "PatchUploadId={PatchUploadId}",
                    AwdpFixExecutionStage.ArchiveDownload,
                    download,
                    work.TargetReceipt.Provider,
                    message.GameplayFactId,
                    message.RuntimeInstanceId,
                    message.PatchUploadId);
                outcome = AwdpFixOutcome.PlatformFailed;
            }
            else
            {
                try
                {
                    await using var source = File.OpenRead(archivePath);
                    await preparer.PrepareTarAsync(
                        source,
                        work.Archive.OriginalFileName,
                        work.PatchEntrypoint,
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
                    throw new AwdpFixPlatformException(
                        AwdpFixExecutionStage.ArchivePreparation,
                        AwdpFixPlatformFailureCode.ArchivePreparationFailed,
                        work.TargetReceipt.Provider,
                        exception);
                }
                var sandbox = providers.Sandbox(work.TargetReceipt.Provider);
                try
                {
                    await using var targetTar = File.OpenRead(tarPath);
                    await sandbox.CopyArchiveAsync(
                        work.TargetReceipt,
                        targetTar,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new AwdpFixPlatformException(
                        AwdpFixExecutionStage.TargetInputPreparation,
                        AwdpFixPlatformFailureCode.TargetInputInjectionFailed,
                        work.TargetReceipt.Provider,
                        exception);
                }

                ContainerExecResult patch;
                try
                {
                    patch = await sandbox.ExecAsync(
                        work.TargetReceipt,
                        work.PatchCommand,
                        work.PatchTimeout,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new AwdpFixPlatformException(
                        AwdpFixExecutionStage.TargetPatchExecution,
                        AwdpFixPlatformFailureCode.TargetPatchExecutionFailed,
                        work.TargetReceipt.Provider,
                        exception);
                }
                if (patch.TimedOut)
                {
                    logger.LogWarning(
                        "AWDP Target Patch did not complete. Stage={Stage} FailureCode={FailureCode} "
                        + "Provider={Provider} GameplayFactId={GameplayFactId} "
                        + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                        AwdpFixExecutionStage.TargetPatchExecution,
                        AwdpFixOutcome.PatchTimeout,
                        work.TargetReceipt.Provider,
                        message.GameplayFactId,
                        message.RuntimeInstanceId,
                        message.PatchUploadId);
                    outcome = AwdpFixOutcome.PatchTimeout;
                }
                else if (patch.ExitCode != 0)
                {
                    logger.LogWarning(
                        "AWDP Target Patch did not complete. Stage={Stage} FailureCode={FailureCode} "
                        + "Provider={Provider} GameplayFactId={GameplayFactId} "
                        + "RuntimeInstanceId={RuntimeInstanceId} PatchUploadId={PatchUploadId}",
                        AwdpFixExecutionStage.TargetPatchExecution,
                        AwdpFixOutcome.PatchFailed,
                        work.TargetReceipt.Provider,
                        message.GameplayFactId,
                        message.RuntimeInstanceId,
                        message.PatchUploadId);
                    outcome = AwdpFixOutcome.PatchFailed;
                }
                else
                {
                    AwdpCheckerExecutionOutcome execution;
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
                            cancellationToken);
                    }
                    if (execution != AwdpCheckerExecutionOutcome.Completed)
                    {
                        logger.LogWarning(
                            "AWDP Checker did not complete normally. Stage={Stage} "
                            + "FailureCode={FailureCode} Provider={Provider} "
                            + "GameplayFactId={GameplayFactId} RuntimeInstanceId={RuntimeInstanceId} "
                            + "PatchUploadId={PatchUploadId}",
                            AwdpFixExecutionStage.CheckerExecution,
                            execution,
                            work.Checker.Provider,
                            message.GameplayFactId,
                            message.RuntimeInstanceId,
                            message.PatchUploadId);
                    }
                    outcome = AwdpCheckerCompletionPolicy.ResultFor(execution);
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
        RunAwdpFixVerification message,
        AwdpFixOutcome outcome)
    {
        using var compensation = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetime.ApplicationStopping);
        compensation.CancelAfter(TimeSpan.FromSeconds(
            AwdpFixExecutionBudget.ResultPublicationBudgetSeconds));
        await outbox.PublishAsync(AwdpFixResult.Create(
            message.GameplayFactId,
            message.RuntimeInstanceId,
            outcome,
            timeProvider.GetUtcNow())).AsTask().WaitAsync(compensation.Token);
        await outbox.FlushOutgoingMessagesAsync().WaitAsync(compensation.Token);
    }

    private async Task RecoverAsync(
        AwdpFixRecoveryWork recovery,
        CancellationToken cancellationToken)
    {
        var identity = new RuntimeResourceIdentity(recovery.RuntimeInstanceId);
        if (!string.IsNullOrWhiteSpace(recovery.ProviderReceiptJson))
        {
            await RuntimeReceiptCleanup.CleanupContainerAsync(
                providers,
                identity,
                recovery.Provider,
                recovery.ProviderReceiptJson,
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
                    "AWDP verification resources remain after identity-based cleanup.");
        }

        TryDeleteOperationDirectory(recovery.GameplayFactId, recovery.RuntimeInstanceId);
        var release = await capacity.ReleaseAsync(
            recovery.RuntimeInstanceId,
            recovery.RunnerId,
            cancellationToken);
        if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            throw new InvalidOperationException(
                "AWDP target capacity belongs to a different Runner assignment.");

        await outbox.PublishAsync(new CompleteAwdpFixRecovery(
            recovery.GameplayFactId,
            recovery.RuntimeInstanceId,
            recovery.RunnerId,
            timeProvider.GetUtcNow()));
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static void TryDeleteOperationDirectory(
        Guid gameplayFactId,
        Guid runtimeInstanceId)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "noctf-awdp",
            $"{gameplayFactId:N}-{runtimeInstanceId:N}");
        TryDeleteDirectory(path);
    }

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

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
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

public sealed record AwdpCheckerWork(
    Guid RuntimeInstanceId,
    int Generation,
    RuntimeProvider Provider,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    string NetworkId,
    string TargetHost,
    int TargetReadyTimeoutSeconds,
    Uri CallbackUrl,
    string CallbackToken,
    TimeSpan Timeout);

public sealed record AwdpFixWork(
    long RuntimeProcessingVersion,
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
    int Generation,
    long RecoveryProcessingVersion,
    RuntimeProvider Provider,
    string? ProviderReceiptJson,
    string RunnerPool,
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
        return command is not { Count: > 0 }
            ? ["/bin/sh", entrypoint]
            : [.. command.Select(argument =>
                string.Equals(argument, "{entrypoint}", StringComparison.Ordinal)
                    ? entrypoint
                    : argument)];
    }
}

public sealed class AwdpFixWorkReader(
    IServiceScopeFactory scopes,
    IRunnerScoringTokenIssuer tokens,
    IConfiguration configuration,
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
            message.Generation,
            message.RuntimeProcessingVersion,
            message.Deadline,
            message.RunnerPool,
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
                    fenceResult.Generation,
                    fenceResult.RuntimeProcessingVersion,
                    fenceResult.Provider,
                    fenceResult.ProviderReceiptJson,
                    fenceResult.RunnerPool,
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
                        && runtime.Purpose == RuntimePurpose.AwdpTarget
                        && runtime.GameplayFactId == message.GameplayFactId
                        && runtime.Generation == message.Generation
                        && runtime.ProcessingVersion == fenceResult.RuntimeProcessingVersion
                        && runtime.State == RuntimeState.Running
                        && runtime.RunnerPool == message.RunnerPool
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
                    challenge.ChallengeId,
                    challenge.Revision
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
                    item.Revision,
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
                    CompetitionConfigurationJson = competition.ConfigurationJson
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || timeProvider.GetUtcNow() >= message.Deadline
            || string.IsNullOrWhiteSpace(target.Runtime.ProviderReceiptJson))
            return new(AwdpFixExecutionFenceDisposition.Superseded);

        var settings = AwdpConfigurationResolver.Resolve(
            target.CompetitionConfigurationJson,
            target.ChallengeRulesJson,
            target.ChallengeDefinitionJson);
        if (settings.Checker is not { } checker)
            return new(AwdpFixExecutionFenceDisposition.Superseded);
        var receipt = JsonSerializer.Deserialize<ContainerReceipt>(
            target.Runtime.ProviderReceiptJson);
        if (receipt?.NetworkId is not { Length: > 0 } networkId
            || receipt.InternalHost is not { Length: > 0 } targetHost)
            return new(AwdpFixExecutionFenceDisposition.Superseded);

        var callbackBase = configuration["RunnerScoring:CallbackBaseUrl"];
        if (!Uri.TryCreate(callbackBase, UriKind.Absolute, out var baseUri)
            || (!string.Equals(
                    baseUri.Scheme,
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    baseUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be configured as an absolute HTTP(S) URI.");
        var now = timeProvider.GetUtcNow();
        var remaining = message.Deadline - now;
        if (remaining <= TimeSpan.Zero)
            return new(AwdpFixExecutionFenceDisposition.Superseded);
        var checkerTimeout = TimeSpan.FromSeconds(checker.TimeoutSeconds);
        var timeout = checkerTimeout < remaining ? checkerTimeout : remaining;
        var callbackToken = tokens.IssueAwdpFixResult(new(
            message.RunnerId,
            message.GameplayFactId,
            message.RuntimeInstanceId,
            message.Generation,
            fenceResult.RuntimeProcessingVersion,
            message.Deadline,
            now));
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
                fenceResult.RuntimeProcessingVersion,
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
                    message.Generation,
                    target.Runtime.RuntimeProvider,
                    checker.Image,
                    checker.Command ?? [],
                    checker.Environment ?? new Dictionary<string, string>(),
                    networkId,
                    targetHost,
                    settings.ReadyTimeoutSeconds,
                    new Uri(baseUri, "/api/internal/v1/awdp/fix-results"),
                    callbackToken,
                    timeout)));
    }
}

public sealed class AwdpCheckerExecutor(IOneShotRuntimeProviderCatalog providers)
    : IAwdpCheckerExecutor
{
    public async Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
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
            new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            work.Timeout,
            NetworkName: work.NetworkId,
            OperationTimeout: work.Timeout,
            AllowInternalCallback: true,
            Generation: work.Generation,
            RuntimeInstanceId: work.RuntimeInstanceId,
            NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(work.Timeout);
        try
        {
            var result = await providers.OneShot(work.Provider).RunAsync(request, timeout.Token);
            return result.ExitCode == 0
                ? AwdpCheckerExecutionOutcome.Completed
                : AwdpCheckerExecutionOutcome.AbnormalExit;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return AwdpCheckerExecutionOutcome.TimedOut;
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
    IAwdpFixExecutionFence executionFence,
    IEnumerable<IRuntimeManagedResourceReconciler> resourceReconcilers,
    IRunnerCapacityGate capacity,
    ITransactionalMessageOutbox outbox,
    IOptions<RunnerOptions> runnerOptions,
    ILogger<AwdpFixVerificationHandler> logger)
{
    public async Task Handle(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            runnerOptions.Value.Pool,
            runnerOptions.Value.Id);
        var claim = await reader.ClaimAsync(message, cancellationToken);
        if (claim.Disposition == AwdpFixExecutionFenceDisposition.Recover)
        {
            await RecoverAsync(
                claim.Recovery
                    ?? throw new InvalidOperationException("AWDP recovery work is unavailable."),
                cancellationToken);
            return;
        }
        var work = claim.Work;
        if (claim.Disposition != AwdpFixExecutionFenceDisposition.Execute
            || work is null)
            return;

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
                    + "FailureCode={FailureCode} GameplayFactId={GameplayFactId} "
                    + "RuntimeInstanceId={RuntimeInstanceId} Generation={Generation}",
                    download,
                    message.GameplayFactId,
                    message.RuntimeInstanceId,
                    message.Generation);
                outcome = AwdpFixOutcome.PlatformFailed;
            }
            else
            {
                await using var source = File.OpenRead(archivePath);
                await preparer.PrepareTarAsync(
                    source,
                    work.Archive.OriginalFileName,
                    work.PatchEntrypoint,
                    workDirectory,
                    tarPath,
                    cancellationToken);
                var sandbox = providers.Sandbox(work.TargetReceipt.Provider);
                await using (var tar = File.OpenRead(tarPath))
                    await sandbox.CopyArchiveAsync(
                        work.TargetReceipt, tar, cancellationToken);
                var patch = await sandbox.ExecAsync(
                    work.TargetReceipt,
                    work.PatchCommand,
                    work.PatchTimeout,
                    cancellationToken);
                if (patch.TimedOut)
                    outcome = AwdpFixOutcome.PatchTimeout;
                else if (patch.ExitCode != 0)
                    outcome = AwdpFixOutcome.PatchFailed;
                else
                {
                    var advanced = await executionFence.TryAdvanceStageAsync(new(
                        message.GameplayFactId,
                        message.RuntimeInstanceId,
                        message.Generation,
                        work.RuntimeProcessingVersion,
                        AwdpFixStage.PatchApplying,
                        AwdpFixStage.CheckerRunning), cancellationToken);
                    if (!advanced)
                        return;
                    var execution = await checker.ExecuteAsync(
                        work.Checker, cancellationToken);
                    outcome = AwdpCheckerCompletionPolicy.ResultFor(execution);
                }
            }
        }
        catch (InvalidDataException)
        {
            outcome = AwdpFixOutcome.PatchFailed;
        }
        catch (FileNotFoundException)
        {
            outcome = AwdpFixOutcome.PlatformFailed;
        }
        finally
        {
            TryDeleteDirectory(operationDirectory);
        }

        if (outcome is not AwdpFixOutcome result)
            return;
        await outbox.PublishAsync(AwdpFixResult.Create(
            message.GameplayFactId,
            message.RuntimeInstanceId,
            message.Generation,
            work.RuntimeProcessingVersion,
            result,
            DateTimeOffset.UtcNow));
        await outbox.FlushOutgoingMessagesAsync();
    }

    private async Task RecoverAsync(
        AwdpFixRecoveryWork recovery,
        CancellationToken cancellationToken)
    {
        var identity = new RuntimeResourceIdentity(
            recovery.RuntimeInstanceId,
            recovery.Generation);
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
            recovery.Generation,
            recovery.RecoveryProcessingVersion,
            recovery.RunnerPool,
            recovery.RunnerId,
            DateTimeOffset.UtcNow));
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

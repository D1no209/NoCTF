using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Storage;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
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
    int TargetPort,
    int TargetReadyTimeoutSeconds,
    Uri CallbackUrl,
    string CallbackToken,
    TimeSpan Timeout);

public sealed record AwdpFixWork(
    string ObjectKey,
    string OriginalFileName,
    long ByteLength,
    byte[] Sha256,
    ContainerReceipt TargetReceipt,
    string PatchEntrypoint,
    IReadOnlyList<string> PatchCommand,
    TimeSpan PatchTimeout,
    AwdpCheckerWork Checker);

public interface IAwdpFixWorkReader
{
    Task<AwdpFixWork?> ReadAsync(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken);
}

public interface IAwdpCheckerExecutor
{
    Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
        AwdpCheckerWork work,
        CancellationToken cancellationToken);
}

public enum AwdpCheckerExecutionOutcome
{
    Completed,
    TimedOut
}

public static class AwdpCheckerCompletionPolicy
{
    public static AwdpFixOutcome? ResultFor(AwdpCheckerExecutionOutcome outcome) => outcome switch
    {
        AwdpCheckerExecutionOutcome.Completed => null,
        AwdpCheckerExecutionOutcome.TimedOut => AwdpFixOutcome.PlatformFailed,
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
    public async Task<AwdpFixWork?> ReadAsync(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var target = await db.Submissions.AsNoTracking()
            .Where(submission => submission.Id == message.SubmissionId
                && submission.CompetitionChallengeId == message.CompetitionChallengeId
                && submission.Kind == SubmissionKind.Fix
                && submission.EvaluationState == SubmissionEvaluationState.Processing
                && submission.ProcessingVersion == message.ProcessingVersion
                && submission.PatchUploadId == message.PatchUploadId)
            .Join(
                db.PatchUploads.AsNoTracking(),
                submission => submission.PatchUploadId,
                upload => upload.Id,
                (submission, upload) => new { Submission = submission, Upload = upload })
            .Join(
                db.RuntimeInstances.AsNoTracking()
                    .Where(runtime => runtime.Id == message.RuntimeInstanceId
                        && runtime.Purpose == RuntimePurpose.AwdpTarget
                        && runtime.SubmissionId == message.SubmissionId
                        && runtime.SubmissionProcessingVersion == message.ProcessingVersion
                        && runtime.Generation == message.Generation
                        && runtime.ProcessingVersion == message.RuntimeProcessingVersion
                        && runtime.State == RuntimeState.Running
                        && runtime.RunnerPool == message.RunnerPool
                        && runtime.RunnerId == message.RunnerId),
                pair => pair.Submission.Id,
                runtime => runtime.SubmissionId,
                (pair, runtime) => new { pair.Submission, pair.Upload, Runtime = runtime })
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                item => item.Submission.CompetitionChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.Submission,
                    item.Upload,
                    item.Runtime,
                    ChallengeConfigurationJson = challenge.ConfigurationJson,
                    challenge.Revision
                })
            .Join(
                db.Competitions.AsNoTracking(),
                item => item.Submission.CompetitionId,
                competition => competition.Id,
                (item, competition) => new
                {
                    item.Upload,
                    item.Runtime,
                    item.ChallengeConfigurationJson,
                    CompetitionConfigurationJson = competition.ConfigurationJson,
                    CompetitionConfigurationRevision = competition.ConfigurationRevision,
                    item.Revision
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Runtime.ConfigurationRevision != target.Revision
            || target.Runtime.CompetitionConfigurationRevision
                != target.CompetitionConfigurationRevision
            || timeProvider.GetUtcNow() >= message.Deadline
            || string.IsNullOrWhiteSpace(target.Runtime.ProviderReceiptJson))
            return null;

        var settings = AwdpConfigurationResolver.Resolve(
            target.CompetitionConfigurationJson,
            target.ChallengeConfigurationJson);
        if (settings.Checker is not { } checker
            || settings.TargetPort is < 1 or > 65535)
            return null;
        var receipt = JsonSerializer.Deserialize<ContainerReceipt>(
            target.Runtime.ProviderReceiptJson);
        if (receipt?.NetworkId is not { Length: > 0 } networkId
            || receipt.InternalHost is not { Length: > 0 } targetHost)
            return null;

        var callbackBase = configuration["RunnerScoring:CallbackBaseUrl"]
            ?? "http://noctf-awdp-callback:8080";
        if (!Uri.TryCreate(callbackBase, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException(
                "RunnerScoring:CallbackBaseUrl must be an absolute URI.");
        var now = timeProvider.GetUtcNow();
        var remaining = message.Deadline - now;
        if (remaining <= TimeSpan.Zero)
            return null;
        var checkerTimeout = TimeSpan.FromSeconds(checker.TimeoutSeconds);
        var timeout = checkerTimeout < remaining ? checkerTimeout : remaining;
        var callbackToken = tokens.IssueAwdpFixResult(new(
            message.RunnerId,
            message.SubmissionId,
            message.RuntimeInstanceId,
            message.Generation,
            message.ProcessingVersion,
            message.RuntimeProcessingVersion,
            message.Deadline,
            now));
        var patchCommand = AwdpPatchCommand.Create(
            settings.PatchCommand,
            settings.PatchEntrypoint);
        return new(
            target.Upload.ObjectKey,
            target.Upload.OriginalFileName,
            target.Upload.ByteLength,
            target.Upload.Sha256,
            receipt,
            settings.PatchEntrypoint,
            patchCommand,
            TimeSpan.FromSeconds(settings.PatchTimeoutSeconds),
            new(
                message.RuntimeInstanceId,
                message.Generation,
                checker.Provider,
                checker.Image,
                checker.Command ?? [],
                checker.Environment ?? new Dictionary<string, string>(),
                networkId,
                targetHost,
                settings.TargetPort,
                settings.ReadyTimeoutSeconds,
                new Uri(baseUri, "/api/internal/v1/awdp/fix-results"),
                callbackToken,
                timeout));
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
            ["TARGET_PORT"] = work.TargetPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/runtime-instance-id"] = work.RuntimeInstanceId.ToString("D"),
                ["noctf.io/generation"] = work.Generation.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["noctf.io/job-kind"] = "awdp-verification",
                ["noctf.io/purpose"] = "awdp-checker"
            },
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
            _ = await providers.OneShot(work.Provider).RunAsync(request, timeout.Token);
            return AwdpCheckerExecutionOutcome.Completed;
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
    IObjectStorage objects,
    FixArchivePreparer preparer,
    IRuntimeProviderCatalog providers,
    IAwdpCheckerExecutor checker,
    ITransactionalMessageOutbox outbox,
    IConfiguration configuration)
{
    public async Task Handle(
        RunAwdpFixVerification message,
        CancellationToken cancellationToken)
    {
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuration["Runner:Pool"] ?? "default",
            configuration["Runner:Id"]
                ?? throw new InvalidOperationException("Runner:Id is required."));
        if (DateTimeOffset.UtcNow >= message.Deadline)
            return;
        var work = await reader.ReadAsync(message, cancellationToken);
        if (work is null)
            return;

        AwdpFixOutcome? outcome = AwdpFixOutcome.PlatformFailed;
        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "noctf-awdp",
            $"{message.SubmissionId:N}-{message.RuntimeInstanceId:N}");
        var workDirectory = Path.Combine(operationDirectory, "work");
        var tarPath = Path.Combine(operationDirectory, "fix.tar");
        try
        {
            var stored = await objects.InspectAsync(work.ObjectKey, cancellationToken);
            if (stored is null
                || stored.Length != work.ByteLength
                || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(stored.Sha256), work.Sha256))
            {
                outcome = AwdpFixOutcome.PlatformFailed;
            }
            else
            {
                await using var source = await objects.OpenReadAsync(
                    work.ObjectKey, cancellationToken);
                await preparer.PrepareTarAsync(
                    source,
                    work.OriginalFileName,
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
                    var execution = await checker.ExecuteAsync(
                        work.Checker, cancellationToken);
                    outcome = AwdpCheckerCompletionPolicy.ResultFor(execution);
                }
            }
        }
        catch (InvalidDataException)
        {
            outcome = AwdpFixOutcome.RuleViolation;
        }
        catch (FileNotFoundException)
        {
            outcome = AwdpFixOutcome.PlatformFailed;
        }
        finally
        {
            if (Directory.Exists(operationDirectory))
                Directory.Delete(operationDirectory, recursive: true);
        }

        if (outcome is not AwdpFixOutcome result)
            return;
        await outbox.PublishAsync(AwdpFixResult.Create(
            message.SubmissionId,
            message.RuntimeInstanceId,
            message.Generation,
            message.ProcessingVersion,
            message.RuntimeProcessingVersion,
            result,
            DateTimeOffset.UtcNow));
        await outbox.FlushOutgoingMessagesAsync();
    }
}

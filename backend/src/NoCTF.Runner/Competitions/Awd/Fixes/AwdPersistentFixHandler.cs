using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Competitions.Awd.Fixes;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Runner.Competitions.Awd.Fixes;

[NonTransactional]
public sealed class AwdPersistentFixHandler(IAwdPersistentFixStore store, FixArchiveDownloader archives,
    FixArchivePreparer preparer, ExecuteFix fix, IRunnerScoringTokenIssuer tokens,
    IOptions<RunnerScoringOptions> scoring, IOptions<RunnerOptions> runner, IOptions<FixVerificationOptions> verification,
    IRunnerCapacityGate capacity, RunnerResourceMutationCoordinator mutations, TimeProvider clock,
    IHostApplicationLifetime lifetime, ILogger<AwdPersistentFixHandler> logger)
{
    public async Task Handle(RunPersistentFix message, CancellationToken ct)
    {
        RunnerNodeAssignmentGuard.Validate(message, runner.Value.Pool, runner.Value.Id);
        if (!await capacity.CanMutateAsync(message.RuntimeInstanceId, runner.Value.Id, ct)) return;
        var work = await store.ClaimAsync(message, clock.GetUtcNow(), ct);
        if (work is null) return;
        var directory = Path.GetFullPath(Path.Combine(verification.Value.WorkDirectory, $"{work.GameplayFactId:N}-{work.RuntimeInstanceId:N}"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "upload.tar.gz");
        var tarPath = Path.Combine(directory, "prepared.tar");
        var externalExecutionStarted = false;
        IDisposable? targetMutation = null;
        FixOutcome outcome;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = work.Deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero) deadline.Cancel(); else deadline.CancelAfter(remaining);
        try
        {
            var origin = scoring.Value.CallbackBaseUrl ?? throw new InvalidOperationException("RunnerScoring:CallbackBaseUrl is required.");
            var archive = new FixArchive(new Uri(origin, $"/api/internal/v1/fixes/{work.GameplayFactId:D}/archive"),
                tokens.IssueFixArchiveRead(runner.Value.Id, work.Archive.UploadId, work.GameplayFactId, clock.GetUtcNow()),
                work.Archive.FileName, work.Archive.ByteLength, work.Archive.Sha256);
            var download = await archives.DownloadAsync(archive, sourcePath, deadline.Token);
            if (download != FixArchiveDownloadOutcome.Downloaded)
                outcome = Failure(work, CapabilityExecutionFailure.InvalidEvidence, false);
            else
            {
                await using (var source = File.OpenRead(sourcePath))
                    await preparer.PrepareTarAsync(source, work.Archive.FileName, work.Definition.Entrypoint,
                        Path.Combine(directory, "extracted"), tarPath, deadline.Token);
                await using var prepared = File.OpenRead(tarPath);
                targetMutation = await mutations.EnterRuntimeAsync(message.RuntimeInstanceId, deadline.Token);
                externalExecutionStarted = true;
                var result = await fix.ExecuteAsync(new(work.GameplayFactId, work.RuntimeInstanceId, work.Definition,
                    work.Receipt.ServiceReceipt(work.Definition.TargetServiceName), prepared, work.Deadline, FixTargetKind.Persistent), deadline.Token);
                outcome = result.Value?.ToGameplayOutcome() ?? Failure(work, result.FailureCode ?? CapabilityExecutionFailure.InvalidInput, false);
            }
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        { targetMutation?.Dispose(); Cleanup(directory, work.GameplayFactId); throw; }
        catch (OperationCanceledException) { outcome = Failure(work, CapabilityExecutionFailure.ExecutionInterrupted, externalExecutionStarted); }
        catch (InvalidDataException)
        {
            outcome = new() { GameplayFactId = work.GameplayFactId, RuntimeInstanceId = work.RuntimeInstanceId,
                ObservedAt = clock.GetUtcNow(), ExecutionState = CapabilityExecutionState.Completed, Result = FixResult.Failed };
        }
        catch (Exception exception)
        {
            logger.LogError("Persistent Fix failed with {FailureType} for {GameplayFactId} and Runtime {RuntimeInstanceId}.",
                exception.GetType().Name, work.GameplayFactId, work.RuntimeInstanceId);
            outcome = Failure(work, CapabilityExecutionFailure.ExecutionInterrupted, externalExecutionStarted);
        }
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        completion.CancelAfter(TimeSpan.FromSeconds(PersistentFixExecutionBudget.CompletionSeconds));
        try { await store.CompleteAsync(outcome, clock.GetUtcNow(), completion.Token); }
        finally
        {
            targetMutation?.Dispose();
            Cleanup(directory, work.GameplayFactId);
        }
    }

    private FixOutcome Failure(PersistentFixWork work, CapabilityExecutionFailure reason, bool uncertain) => new()
    {
        GameplayFactId = work.GameplayFactId, RuntimeInstanceId = work.RuntimeInstanceId, ObservedAt = clock.GetUtcNow(),
        ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = reason, ExecutionUncertain = uncertain
    };

    private void Cleanup(string directory, Guid factId)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { logger.LogWarning("Persistent Fix scratch cleanup failed with {FailureType} for {GameplayFactId}.", exception.GetType().Name, factId); }
    }
}

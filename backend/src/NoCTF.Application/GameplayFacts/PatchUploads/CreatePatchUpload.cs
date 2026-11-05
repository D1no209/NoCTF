using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Common;
using NoCTF.Application.Storage;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.GameplayFacts.PatchUploads;

public sealed record PatchUploadScope(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid UserId,
    Guid RuntimeInstanceId,
    long MaximumArchiveBytes = PatchUploadRules.DefaultMaximumArchiveBytes,
    RuntimePurpose Purpose = RuntimePurpose.FixTarget,
    NoCTF.Domain.Challenges.Capabilities.FixTargetKind TargetKind = NoCTF.Domain.Challenges.Capabilities.FixTargetKind.Isolated,
    Guid? ExecutionScopeId = null);

public static class PatchUploadRules
{
    public const long DefaultMaximumArchiveBytes = 256L * 1024 * 1024;
    public const long HardMaximumArchiveBytes = 1024L * 1024 * 1024;
}

public sealed record AcceptedFixSubmission(
    Guid PatchUploadId,
    Guid GameplayFactId,
    NoCTF.Domain.Gameplay.GameplayFactState State);

public enum PatchUploadFailureCode
{
    PatchUploadNotAvailable,
    ArchiveStreamNotSeekable,
    ArchiveTooLarge,
    ArchiveInvalid,
    DefenseTargetNotReady,
    DefenseAlreadySucceeded,
    AttemptsExhausted,
    DefenseTargetConsumed,
    PatchUploadConflict,
    ChallengeNotOpened,
    ChallengeSubmissionClosed
}

public enum PatchUploadSaveState
{
    Accepted,
    DefenseTargetNotReady,
    AchievementAlreadySucceeded,
    DefenseTargetConsumed,
    AttemptsExhausted,
    ConcurrencyConflict,
    ChallengeNotOpened,
    ChallengeSubmissionClosed
}

public sealed record PatchUploadSaveResult(
    PatchUploadSaveState State,
    Guid? GameplayFactId = null,
    NoCTF.Domain.Gameplay.GameplayFactState? GameplayFactState = null);

public interface IPatchUploadStore
{
    Task<bool> CanAccessTargetAsync(Guid competitionId, Guid challengeId, Guid targetId, Guid userId, CancellationToken ct) => Task.FromResult(false);
    Task<bool> IsTargetUnconsumedAsync(PatchUploadScope scope, CancellationToken ct) => Task.FromResult(true);
    Task<PatchUploadScope?> ResolveScopeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        CancellationToken cancellationToken);
    Task<PatchUploadSaveResult> SaveAsync(
        Guid patchUploadId,
        Guid gameplayFactId,
        PatchUploadScope scope,
        Guid fileId,
        DateTimeOffset uploadedAt,
        CancellationToken cancellationToken);
}

public sealed class CreatePatchUpload(
    IPatchUploadStore store,
    ManagedFileUploads uploads,
    IRequestReplay? replay = null)
{
    public async Task<OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        Guid userId,
        string fileName,
        string contentType,
        Stream content,
        DateTimeOffset now,
        CancellationToken ct = default,
        RuntimePurpose? expectedPurpose = null,
        Guid? expectedExecutionScopeId = null)
    {
        if (content.CanSeek && content.Length <= PatchUploadRules.HardMaximumArchiveBytes && replay is not null)
        {
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(content, ct);
            content.Position = 0;
            var previous = await replay.FindAsync<AcceptedFixSubmission>(
                new(userId, ReplayOperation.PatchUpload, competitionId, runtimeInstanceId),
                new PatchUploadReplayFingerprint(competitionChallengeId, fileName, contentType,
                    content.Length, Convert.ToHexString(hash)), ct);
            if (previous is not null) return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Success(previous);
        }
        var scope = await store.ResolveScopeAsync(
            competitionId, competitionChallengeId, runtimeInstanceId, userId, ct);
        if (scope is null)
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.PatchUploadNotAvailable,
                "No active one-shot defense target is available for this team and challenge.");
        if (expectedPurpose is { } purpose && scope.Purpose != purpose)
        {
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.PatchUploadNotAvailable,
                "The Patch target does not belong to this verification workflow.");
        }
        if (expectedExecutionScopeId is { } expectedScope && scope.ExecutionScopeId != expectedScope)
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(PatchUploadFailureCode.PatchUploadNotAvailable,
                "The Fix target does not belong to this execution scope.");
        if (!content.CanSeek)
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveStreamNotSeekable,
                "The archive stream must support validation before storage.");
        if (content.Length > scope.MaximumArchiveBytes)
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveTooLarge,
                $"The archive exceeds the configured {scope.MaximumArchiveBytes}-byte upload limit.");

        if (!await store.IsTargetUnconsumedAsync(scope, ct))
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.DefenseTargetConsumed, "This defense target already accepted a Patch or is no longer active.");

        var validation = FixArchiveValidation.Validate(content);
        ct.ThrowIfCancellationRequested();
        content.Position = 0;
        if (validation is not null)
            return OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.ArchiveInvalid, validation);

        var id = Guid.CreateVersion7(now);
        var gameplayFactId = Guid.CreateVersion7(now.AddTicks(1));
        var fileId = Guid.CreateVersion7(now);
        var uploaded = await uploads.CreateAsync(
            fileId,
            $"fix-uploads/{id:N}",
            Path.GetFileName(fileName),
            string.IsNullOrWhiteSpace(contentType)
                ? "application/gzip"
                : contentType,
            content,
            now,
            ct);
        PatchUploadSaveResult? saved = null;
        try
        {
            saved = await store.SaveAsync(
                id,
                gameplayFactId,
                scope,
                uploaded.FileId,
                now,
                ct);
        }
        finally
        {
            if (saved?.State != PatchUploadSaveState.Accepted)
                await uploads.AbandonAsync(uploaded.FileId);
        }
        return saved!.State switch
        {
            PatchUploadSaveState.Accepted =>
                OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Success(
                    new(
                        id,
                        saved.GameplayFactId!.Value,
                        saved.GameplayFactState!.Value)),
            PatchUploadSaveState.ChallengeNotOpened => OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(PatchUploadFailureCode.ChallengeNotOpened, "The challenge is not open."),
            PatchUploadSaveState.ChallengeSubmissionClosed => OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(PatchUploadFailureCode.ChallengeSubmissionClosed, "Challenge submissions are closed."),
            PatchUploadSaveState.DefenseTargetNotReady =>
                OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.DefenseTargetNotReady,
                    "The one-shot defense target no longer accepts a patch."),
            PatchUploadSaveState.AchievementAlreadySucceeded =>
                OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.DefenseAlreadySucceeded,
                    "This AWDP defense achievement has already succeeded."),
            PatchUploadSaveState.AttemptsExhausted =>
                OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.AttemptsExhausted,
                    "The maximum number of Fix attempts has been reached."),
            PatchUploadSaveState.DefenseTargetConsumed =>
                OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                    PatchUploadFailureCode.DefenseTargetConsumed,
                    "The one-shot defense target already consumed its only patch."),
            _ => OperationResult<AcceptedFixSubmission, PatchUploadFailureCode>.Failure(
                PatchUploadFailureCode.PatchUploadConflict,
                "The one-shot defense target was consumed concurrently.")
        };
    }

}

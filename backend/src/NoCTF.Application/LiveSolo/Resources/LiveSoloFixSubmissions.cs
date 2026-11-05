using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Storage;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.LiveSolo.Resources;

public sealed record LiveSoloFixTargetResult(Guid? RuntimeInstanceId = null, RuntimeState? State = null, Guid? GameplayFactId = null,
    LiveSoloFailure? Failure = null, long? AdmissionSequence = null);
public interface ILiveSoloFixTargetStore
{
    Task<LiveSoloFixTargetResult> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct);
    Task<LiveSoloFixTargetResult> RequestAsync(LiveSoloResourceRequest request, CancellationToken ct);
}

public interface ILiveSoloFixUploadStore : IPatchUploadStore
{
    Task<Guid?> ResolveChallengeAsync(LiveSoloResourceRequest request, CancellationToken ct);
    Task<LiveSoloAdmissionResult> ReadAcceptanceAsync(LiveSoloResourceRequest request, Guid gameplayFactId, CancellationToken ct);
}

public sealed record LiveSoloFixUploadResult(AcceptedFixSubmission? Upload = null, LiveSoloAdmissionResult? Admission = null,
    PatchUploadFailureCode? Failure = null);

public sealed class ManageLiveSoloFixTargets(ILiveSoloFixTargetStore store)
{
    public Task<LiveSoloFixTargetResult> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct) => store.ReadAsync(request, ct);
    public Task<LiveSoloFixTargetResult> RequestAsync(LiveSoloResourceRequest request, CancellationToken ct) => store.RequestAsync(request, ct);
}

public sealed class SubmitLiveSoloFix(ILiveSoloFixUploadStore store, ManagedFileUploads files, IRequestReplay? replay = null)
{
    public async Task<LiveSoloFixUploadResult> ExecuteAsync(LiveSoloResourceRequest request, Guid runtimeId, string fileName,
        string contentType, Stream content, CancellationToken ct)
    {
        var challengeId = await store.ResolveChallengeAsync(request, ct);
        if (challengeId is null) return new(Failure: PatchUploadFailureCode.PatchUploadNotAvailable);
        var result = await new CreatePatchUpload(store, files, replay).ExecuteAsync(request.CompetitionId, challengeId.Value,
            runtimeId, request.ActorId, fileName, contentType, content, request.Now, ct,
            RuntimePurpose.ScopedFixTarget, request.QuestionId);
        return result.Succeeded ? new(result.Value, await store.ReadAcceptanceAsync(request, result.Value!.GameplayFactId, ct))
            : new(Failure: result.FailureCode);
    }
}

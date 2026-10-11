using FluentStorage.Storage;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.Application.LiveSolo.Resources;

public sealed record LiveSoloReviewRequest(Guid CompetitionId, Guid MatchId, Guid ActorId, bool Staff, DateTimeOffset Now);
public sealed record LiveSoloReviewQuestion(Guid Id, Guid RoundId, Guid CompetitionChallengeId, int RoundNumber, int Replay,
    int Position, string? Title, DateTimeOffset? OpenedAt, bool ContentRecorded);
public sealed record LiveSoloReviewContent(LiveSoloQuestionView Question, IReadOnlyList<LiveSoloHintView> Hints,
    IReadOnlyList<LiveSoloAttachmentView> Attachments, bool Historical);
public sealed record LiveSoloReviewFile(Guid FileId, string ObjectKey, LiveSoloAttachmentView Metadata);
public interface ILiveSoloReviewStore
{
    Task<IReadOnlyList<LiveSoloReviewQuestion>?> ListAsync(LiveSoloReviewRequest request, CancellationToken ct);
    Task<LiveSoloReviewContent?> ReadAsync(LiveSoloReviewRequest request, Guid questionId, CancellationToken ct);
    Task<LiveSoloReviewFile?> FileAsync(LiveSoloReviewRequest request, Guid questionId, Guid attachmentId, CancellationToken ct);
}

public sealed class ReadLiveSoloReview(ILiveSoloReviewStore store, IStore objects)
{
    public Task<IReadOnlyList<LiveSoloReviewQuestion>?> ListAsync(LiveSoloReviewRequest request, CancellationToken ct) => store.ListAsync(request, ct);
    public Task<LiveSoloReviewContent?> ReadAsync(LiveSoloReviewRequest request, Guid questionId, CancellationToken ct) => store.ReadAsync(request, questionId, ct);
    public Task<LiveSoloReviewFile?> FileAsync(LiveSoloReviewRequest request, Guid questionId, Guid attachmentId, CancellationToken ct) => store.FileAsync(request, questionId, attachmentId, ct);
    public async Task<(LiveSoloAttachmentView Metadata, Stream Content)?> OpenAsync(LiveSoloReviewRequest request, Guid questionId, Guid attachmentId, CancellationToken ct)
    {
        var file = await store.FileAsync(request, questionId, attachmentId, ct);
        if (file is null || !await objects.ObjectExists(file.ObjectKey, ct)) return null;
        var stream = await objects.OpenRead(file.ObjectKey, ct);
        if (stream is null) return null;
        try
        {
            if (await store.FileAsync(request, questionId, attachmentId, ct) is not { } current || current.FileId != file.FileId)
            { await stream.DisposeAsync(); return null; }
            return (file.Metadata, stream);
        }
        catch { await stream.DisposeAsync(); throw; }
    }
}

using FluentStorage.Storage;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Application.LiveSolo.Resources;

public sealed record LiveSoloResourceRequest(Guid CompetitionId, Guid MatchId, Guid RoundId, Guid QuestionId, Guid ActorId, DateTimeOffset Now);
public sealed record LiveSoloAttachmentView(Guid Id, string FileName, string ContentType, long ByteLength);
public sealed record LiveSoloAttachmentSet(AttachmentDeliveryPolicy DeliveryPolicy, IReadOnlyList<LiveSoloAttachmentView> Items);
public sealed record LiveSoloAttachmentCandidate(LiveSoloAttachmentView Metadata, Guid TeamId, Guid CompetitionChallengeId, Guid FileId, string ObjectKey);
public sealed record LiveSoloAttachmentStream(LiveSoloAttachmentView? Metadata, Stream? Content, LiveSoloFailure? Failure = null);
public interface ILiveSoloAttachmentStore
{
    Task<LiveSoloAttachmentSet?> ListAsync(LiveSoloResourceRequest request, CancellationToken ct);
    Task<LiveSoloAttachmentCandidate?> SelectAsync(LiveSoloResourceRequest request, Guid? attachmentId, CancellationToken ct);
    Task<bool> RecordDownloadAsync(LiveSoloResourceRequest request, LiveSoloAttachmentCandidate selected, CancellationToken ct);
}

public sealed class AccessLiveSoloAttachments(ILiveSoloAttachmentStore store, IStore objects)
{
    public Task<LiveSoloAttachmentSet?> ListAsync(LiveSoloResourceRequest request, CancellationToken ct) => store.ListAsync(request, ct);
    public async Task<bool> PrepareBrowserAsync(LiveSoloResourceRequest request, Guid? attachmentId, CancellationToken ct)
    {
        var set = await store.ListAsync(request, ct);
        return set is not null && (set.DeliveryPolicy == AttachmentDeliveryPolicy.RandomOnePerTeam
            ? attachmentId is null : attachmentId is Guid id && set.Items.Any(x => x.Id == id));
    }
    public async Task<LiveSoloAttachmentStream> OpenAsync(LiveSoloResourceRequest request, Guid? attachmentId, CancellationToken ct)
    {
        var selected = await store.SelectAsync(request, attachmentId, ct);
        if (selected is null) return new(null, null, LiveSoloFailure.NotFound);
        Stream? content = null;
        try
        {
            if (!await objects.ObjectExists(selected.ObjectKey, ct)) return new(null, null, LiveSoloFailure.NotFound);
            content = await objects.OpenRead(selected.ObjectKey, ct);
            if (content is null) return new(null, null, LiveSoloFailure.DependencyUnavailable);
            if (!await store.RecordDownloadAsync(request, selected, ct))
            { await content.DisposeAsync(); return new(null, null, LiveSoloFailure.NotFound); }
            return new(selected.Metadata, content);
        }
        catch
        {
            if (content is not null) await content.DisposeAsync();
            throw;
        }
    }
}

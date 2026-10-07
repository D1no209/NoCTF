namespace NoCTF.Application.Challenges.Attachments;

/// <summary>Checks resource access without opening the object, assigning a random variant, or recording download evidence.</summary>
public sealed class PrepareChallengeAttachmentDownload(IChallengeAttachmentStore store)
{
    public async Task<bool> ExecuteAsync(Guid competitionId, Guid competitionChallengeId, Guid? attachmentId, Guid actorId, CancellationToken ct)
    {
        var set = await store.ListPlayerAsync(competitionId, competitionChallengeId, actorId, ct);
        if (set is null) return false;
        return set.DeliveryPolicy == AttachmentDeliveryPolicy.RandomOnePerTeam
            ? attachmentId is null
            : attachmentId is { } id && set.Items.Any(item => item.Id == id && item.DeletedAt is null);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private async Task OpenQuestionAsync(LiveSoloRoundQuestion question, DateTimeOffset now, CancellationToken ct)
    {
        if (question.OpenedAt is not null) return;
        var material = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Id == question.CompetitionChallengeId)
            .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (entry, template) => new { Entry = entry, Template = template })
            .SingleAsync(ct);
        var content = new LiveSoloOpenedContent { RoundQuestionId = question.Id,
            Title = material.Entry.CustomTitle ?? material.Template.Title, Description = material.Template.Description,
            Direction = material.Template.Direction,
            RandomAttachment = await db.ChallengeFlags.AnyAsync(x => x.ChallengeId == material.Template.Id && x.DeletedAt == null
                && x.SpecificationKind == SpecificationKind.Attachment && x.SpecificationId != null, ct) };
        content.Tags = material.Entry.Tags.OrderBy(x => x.Position).Select((x, i) =>
            new LiveSoloOpenedTag { RoundQuestionId = question.Id, Position = i, Name = x.Name }).ToList();
        content.Hints = material.Entry.Hints.Where(x => x.Cost == 0 && x.PublishedAt <= now && x.HiddenAt == null)
            .OrderBy(x => x.PublishedAt).ThenBy(x => x.Id).Select((x, i) => new LiveSoloOpenedHint {
                RoundQuestionId = question.Id, HintId = x.Id, Position = i, Content = x.Content, PublishedAt = x.PublishedAt!.Value }).ToList();
        var attachments = await db.Set<ChallengeAttachment>().AsNoTracking().Where(x => x.ChallengeId == material.Template.Id && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        var fileLock = new FileReferenceLock();
        foreach (var attachment in attachments)
        {
            if (!await fileLock.AcquireAsync(db, attachment.FileId, ct))
                throw new InvalidOperationException("Opened material must reference an available immutable file.");
            content.Attachments.Add(new() { RoundQuestionId = question.Id, AttachmentId = attachment.Id,
                FileId = attachment.FileId, Position = content.Attachments.Count });
        }
        question.OpenedContent = content;
        question.OpenedAt = now;
    }
}

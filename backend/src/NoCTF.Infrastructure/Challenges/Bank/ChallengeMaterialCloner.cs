using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Challenges.Bank;

public enum ChallengeMaterialCloneFailure { UnsupportedDefinition, NotFound, AttachmentsRequired }
public sealed record ChallengeMaterialClone(Challenge? Challenge, int FlagCount = 0, ChallengeMaterialCloneFailure? Failure = null)
{
    public IReadOnlyDictionary<Guid, Guid> AttachmentIds { get; init; } = new Dictionary<Guid, Guid>();
}

/// <summary>Copies material inside the caller's transaction; immutable files retain independent business references.</summary>
public sealed class ChallengeMaterialCloner(NoCtfDbContext db, FileReferenceLock files)
{
    public async Task<ChallengeMaterialClone> CloneAsync(Challenge source, Guid ownerId, bool attachments, bool flags, DateTimeOffset now, CancellationToken ct)
    {
        if (!ChallengeTemplateCopyPolicy.Supports(source.Definition)) return new(null, Failure: ChallengeMaterialCloneFailure.UnsupportedDefinition);
        var id = Guid.CreateVersion7(now);
        var copy = new Challenge { Id = id, OwnerId = ownerId, Visibility = ChallengeVisibility.Private,
            Title = source.Title, Description = source.Description, Direction = source.Direction,
            Definition = ChallengeTemplateCopyPolicy.Copy(source.Definition!, id), CreatedAt = now, UpdatedAt = now };
        if (copy.Definition.Questionnaire is { } questionnaire)
        {
            questionnaire.CurrentRevision.CreatedAt = now;
            foreach (var fileId in questionnaire.CurrentRevision.Questions.SelectMany(question => question.Files).Select(file => file.FileId).Distinct())
                if (!await files.AcquireAsync(db, fileId, ct)) return new(null, Failure: ChallengeMaterialCloneFailure.NotFound);
        }
        var attachmentMap = new Dictionary<Guid, Guid>();
        if (attachments)
            foreach (var attachment in source.Attachments.Where(x => x.DeletedAt == null).OrderBy(x => x.Id))
            {
                if (!await files.AcquireAsync(db, attachment.FileId, ct)) return new(null, Failure: ChallengeMaterialCloneFailure.NotFound);
                var attachmentId = Guid.CreateVersion7(); attachmentMap.Add(attachment.Id, attachmentId);
                copy.Attachments.Add(new() { Id = attachmentId, ChallengeId = id, FileId = attachment.FileId, CreatedAt = now });
            }
        var copiedFlags = 0;
        if (flags)
        {
            var values = await db.ChallengeFlags.AsNoTracking().Where(x => x.ChallengeId == source.Id && x.TeamId == null && x.DeletedAt == null
                && (x.SpecificationKind == null || x.SpecificationKind == SpecificationKind.Attachment)).ToArrayAsync(ct);
            if (values.Any(x => x.SpecificationKind == SpecificationKind.Attachment
                && (x.SpecificationId is not Guid attachment || !attachmentMap.ContainsKey(attachment))))
                return new(null, Failure: ChallengeMaterialCloneFailure.AttachmentsRequired);
            foreach (var value in values)
            {
                db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.CreateVersion7(), ChallengeId = id,
                    Flag = value.Flag, FlagSha256 = value.FlagSha256.ToArray(), MatchKind = value.MatchKind,
                    SpecificationKind = value.SpecificationKind,
                    SpecificationId = value.SpecificationId is Guid attachment ? attachmentMap[attachment] : null,
                    ValidStart = value.ValidStart, ValidUntil = value.ValidUntil, CreatedAt = now });
                copiedFlags++;
            }
        }
        var canonical = await db.ChallengeSources.AsNoTracking().Where(x => x.ChallengeId == source.Id)
            .Select(x => (Guid?)x.CanonicalChallengeId).SingleOrDefaultAsync(ct) ?? source.Id;
        db.Challenges.Add(copy);
        db.ChallengeSources.Add(new() { ChallengeId = id, OriginalChallengeId = source.Id,
            CanonicalChallengeId = canonical, CopiedAt = now });
        return new(copy, copiedFlags) { AttachmentIds = attachmentMap };
    }
}

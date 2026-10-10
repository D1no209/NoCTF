using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Templates;

public sealed record LiveSoloMaterialClone(LiveSoloChallenge? Challenge, int FlagCount = 0, LiveSoloPoolFailure? Failure = null)
{
    public IReadOnlyDictionary<Guid, Guid> AttachmentIds { get; init; } = new Dictionary<Guid, Guid>();
}

/// <summary>Copies material inside the caller's transaction; immutable files retain independent business references.</summary>
public sealed class LiveSoloMaterialCloner(NoCtfDbContext db, FileReferenceLock files)
{
    public async Task<LiveSoloMaterialClone> CloneAsync(Challenge source, Guid ownerId, bool attachments, bool flags, DateTimeOffset now, CancellationToken ct)
    {
        if (!LiveSoloTemplateCopyPolicy.Supports(source.Definition)) return new(null, Failure: LiveSoloPoolFailure.UnsupportedDefinition);
        var id = Guid.CreateVersion7(now);
        var copy = new LiveSoloChallenge { Id = id, OwnerId = ownerId, Visibility = ChallengeVisibility.Private,
            Title = source.Title, Description = source.Description, Direction = source.Direction,
            Definition = LiveSoloTemplateCopyPolicy.Copy(source.Definition!, id), CreatedAt = now, UpdatedAt = now };
        var attachmentMap = new Dictionary<Guid, Guid>();
        if (attachments)
            foreach (var attachment in source.Attachments.Where(x => x.DeletedAt == null).OrderBy(x => x.Id))
            {
                if (!await files.AcquireAsync(db, attachment.FileId, ct)) return new(null, Failure: LiveSoloPoolFailure.NotFound);
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
                return new(null, Failure: LiveSoloPoolFailure.InvalidContent);
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
        var canonical = await db.LiveSoloChallengeSources.AsNoTracking().Where(x => x.ChallengeId == source.Id)
            .Select(x => (Guid?)x.CanonicalChallengeId).SingleOrDefaultAsync(ct) ?? source.Id;
        db.Challenges.Add(copy);
        db.LiveSoloChallengeSources.Add(new() { ChallengeId = id, OriginalChallengeId = source.Id,
            CanonicalChallengeId = canonical, CopiedAt = now });
        return new(copy, copiedFlags) { AttachmentIds = attachmentMap };
    }
}

using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Templates;

public sealed class LiveSoloTemplateCopyStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
    IChallengeManagementStore challenges, IChallengeConfigurationCatalog configurations, FileReferenceLock files,
    IPostCommitMessagePublisher messages) : ILiveSoloTemplateCopyStore
{
    public async Task<LiveSoloTemplateCopyResult> CopyAsync(CopyLiveSoloTemplateCommand command, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.ActorId && x.AccountStatus == UserAccountStatus.Active, ct);
                if (actor is null || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct))
                    return new(null, LiveSoloTemplateCopyFailure.Forbidden);
                var ownerId = await db.Competitions.AsNoTracking().Where(x => x.Id == command.CompetitionId && x.Mode == GameMode.LiveSolo)
                    .Select(x => (Guid?)x.OwnerId).SingleOrDefaultAsync(ct);
                if (ownerId is not Guid owner)
                    return new(null, LiveSoloTemplateCopyFailure.NotFound);
                var ownerRole = await db.Users.AsNoTracking().Where(x => x.Id == owner).Select(x => (UserRole?)x.Role).SingleOrDefaultAsync(ct);
                if (ownerRole is null || !ownerRole.Value.CanManageResources()) return new(null, LiveSoloTemplateCopyFailure.Forbidden);
                var source = await db.Challenges.AsNoTracking().Include(x => x.Managers).Include(x => x.Attachments).AsSplitQuery()
                    .SingleOrDefaultAsync(x => x.Id == command.SourceChallengeId, ct);
                if (source is null) return new(null, LiveSoloTemplateCopyFailure.NotFound);
                var writable = actor.Role == UserRole.Administrator || source.OwnerId == actor.Id || source.Managers.Any(x => x.UserId == actor.Id);
                if (!writable && source.Visibility != ChallengeVisibility.Shared) return new(null, LiveSoloTemplateCopyFailure.NotFound);
                if (!LiveSoloTemplateCopyPolicy.Supports(source.Definition)) return new(null, LiveSoloTemplateCopyFailure.UnsupportedSource);
                if (command.CopyAttachments && source.Attachments.Any(x => x.DeletedAt == null) && !writable)
                    return new(null, LiveSoloTemplateCopyFailure.AttachmentsForbidden);
                if (command.CopyFlags && !writable) return new(null, LiveSoloTemplateCopyFailure.FlagsForbidden);

                var id = Guid.CreateVersion7(command.Now);
                var copy = new LiveSoloChallenge { Id = id, OwnerId = owner, Visibility = ChallengeVisibility.Private,
                    Title = source.Title, Description = source.Description, Direction = source.Direction,
                    Definition = LiveSoloTemplateCopyPolicy.Copy(source.Definition!, id), CreatedAt = command.Now, UpdatedAt = command.Now };
                var validation = configurations.ValidateDefinition(copy.Mode, copy.Definition);
                if (validation.Count != 0) return new(null, LiveSoloTemplateCopyFailure.UnsupportedSource);
                var attachmentMap = new Dictionary<Guid, Guid>();
                if (command.CopyAttachments)
                    foreach (var attachment in source.Attachments.Where(x => x.DeletedAt == null).OrderBy(x => x.Id))
                    {
                        if (!await files.AcquireAsync(db, attachment.FileId, ct)) return new(null, LiveSoloTemplateCopyFailure.NotFound);
                        var attachmentId = Guid.CreateVersion7(); attachmentMap.Add(attachment.Id, attachmentId);
                        copy.Attachments.Add(new() { Id = attachmentId, ChallengeId = id, FileId = attachment.FileId, CreatedAt = command.Now });
                    }
                var copiedFlags = 0;
                if (command.CopyFlags)
                {
                    var flags = await db.ChallengeFlags.AsNoTracking().Where(x => x.ChallengeId == source.Id && x.TeamId == null && x.DeletedAt == null
                        && (x.SpecificationKind == null || x.SpecificationKind == SpecificationKind.Attachment)).ToArrayAsync(ct);
                    if (flags.Any(flag => flag.SpecificationKind == SpecificationKind.Attachment
                        && (flag.SpecificationId is not Guid old || !attachmentMap.ContainsKey(old))))
                        return new(null, LiveSoloTemplateCopyFailure.AttachmentsRequired);
                    foreach (var flag in flags)
                    {
                        db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.CreateVersion7(), ChallengeId = id, Flag = flag.Flag,
                            FlagSha256 = flag.FlagSha256.ToArray(), MatchKind = flag.MatchKind, SpecificationKind = flag.SpecificationKind,
                            SpecificationId = flag.SpecificationId is Guid attachment ? attachmentMap[attachment] : null,
                            ValidStart = flag.ValidStart, ValidUntil = flag.ValidUntil, CreatedAt = command.Now });
                        copiedFlags++;
                    }
                }
                var canonical = await db.LiveSoloChallengeSources.AsNoTracking().Where(x => x.ChallengeId == source.Id)
                    .Select(x => (Guid?)x.CanonicalChallengeId).SingleOrDefaultAsync(ct) ?? source.Id;
                db.Challenges.Add(copy); db.LiveSoloChallengeSources.Add(new() { ChallengeId = id, OriginalChallengeId = source.Id,
                    CanonicalChallengeId = canonical, CopiedAt = command.Now });
                await db.SaveChangesAsync(ct);
                var order = (await db.CompetitionChallenges.IgnoreQueryFilters().Where(x => x.CompetitionId == command.CompetitionId)
                    .Select(x => (int?)x.Order).MaxAsync(ct) ?? -1) + 1;
                var entry = await challenges.CreateAsync(new(null, command.CompetitionId, id, order, command.Now, Tags: command.Tags),
                    new LiveSoloCompetitionChallengeRules(), ct);
                if (entry.Failure is not null) { messages.DiscardPendingMessages(); db.ChangeTracker.Clear(); return new(null, LiveSoloTemplateCopyFailure.Conflict); }
                await transaction.CommitAsync(ct); committed = true; await messages.FlushCommittedMessagesAsync();
                return new(new(id, entry.Challenge!.Id, canonical, copy.Title, copy.Attachments.Count, copiedFlags,
                    source.Definition!.Runtime is { Allocation: not PersistedRuntimeAllocation.PerTeam }));
            }
            catch (Exception ex) when (!committed && attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(null, LiveSoloTemplateCopyFailure.Conflict); }
        }
    }
}

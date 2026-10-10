using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Templates;

public sealed class LiveSoloTemplateCopyStore(NoCtfDbContext db, LiveSoloMaterialCloner cloner,
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
                if (actor is null || !actor.Role.CanManageResources()) return new(null, LiveSoloTemplateCopyFailure.Forbidden);
                var source = await db.Challenges.AsNoTracking().Include(x => x.Managers).Include(x => x.Attachments).AsSplitQuery()
                    .SingleOrDefaultAsync(x => x.Id == command.SourceChallengeId && x.CatalogScope == ChallengeCatalogScope.QuestionBank, ct);
                if (source is null) return new(null, LiveSoloTemplateCopyFailure.NotFound);
                var writable = actor.Role == UserRole.Administrator || source.OwnerId == actor.Id || source.Managers.Any(x => x.UserId == actor.Id);
                if (!writable && source.Visibility != ChallengeVisibility.Shared) return new(null, LiveSoloTemplateCopyFailure.NotFound);
                if (source.ConcurrencyStamp != command.ExpectedSourceStamp) return new(null, LiveSoloTemplateCopyFailure.SourceChanged);
                if (source.Mode != GameMode.Ctf || !LiveSoloTemplateCopyPolicy.Supports(source.Definition)) return new(null, LiveSoloTemplateCopyFailure.UnsupportedSource);
                if (command.CopyAttachments && source.Attachments.Any(x => x.DeletedAt == null) && !writable) return new(null, LiveSoloTemplateCopyFailure.AttachmentsForbidden);
                if (command.CopyFlags && !writable) return new(null, LiveSoloTemplateCopyFailure.FlagsForbidden);
                var clone = await cloner.CloneAsync(source, actor.Id, command.CopyAttachments, command.CopyFlags, command.Now, ct);
                if (clone.Challenge is null) return new(null, clone.Failure == LiveSoloPoolFailure.InvalidContent
                    ? LiveSoloTemplateCopyFailure.AttachmentsRequired : LiveSoloTemplateCopyFailure.NotFound);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync();
                var canonical = await db.LiveSoloChallengeSources.Where(x => x.ChallengeId == clone.Challenge.Id).Select(x => x.CanonicalChallengeId).SingleAsync(ct);
                return new(new(clone.Challenge.Id, canonical, clone.Challenge.Title, clone.Challenge.Attachments.Count, clone.FlagCount,
                    source.Definition!.Runtime is { Allocation: not PersistedRuntimeAllocation.PerTeam }));
            }
            catch (Exception ex) when (!committed && attempt < 2 && TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(null, LiveSoloTemplateCopyFailure.Conflict); }
            catch when (!committed) { messages.DiscardPendingMessages(); throw; }
        }
    }
}

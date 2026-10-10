using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloAttachmentStore(NoCtfDbContext db, IExecutionScopeAccess access, TimeProvider clock) : ILiveSoloAttachmentStore
{
    private sealed record Scope(Guid TeamId, Guid CompetitionChallengeId, Guid ChallengeId);
    private async Task<Scope?> AuthorizeAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId && x.Members.Any(m => m.UserId == request.ActorId))
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var question = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.Id == request.QuestionId && x.RoundId == request.RoundId)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId == request.MatchId), x => x.RoundId, x => x.Id, (q, _) => q)
            .Join(db.CompetitionChallenges.AsNoTracking(), x => x.CompetitionChallengeId, x => x.Id, (q, entry) => new { Entry = entry }).SingleOrDefaultAsync(ct);
        if (team is not Guid teamId || question is null || !await access.CanAccessAsync(new(request.QuestionId, request.CompetitionId,
            question.Entry.Id, teamId, request.ActorId, ExecutionScopeOperation.Read, clock.GetUtcNow()), ct)) return null;
        return new(teamId, question.Entry.Id, question.Entry.ChallengeId);
    }
    private Task<bool> RandomAsync(Guid template, CancellationToken ct) => db.ChallengeFlags.AnyAsync(x => x.ChallengeId == template
        && x.SpecificationKind == SpecificationKind.Attachment && x.SpecificationId != null && x.DeletedAt == null, ct);
    public async Task<LiveSoloAttachmentSet?> ListAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        var scope = await AuthorizeAsync(request, ct); if (scope is null) return null;
        if (await RandomAsync(scope.ChallengeId, ct)) return new(AttachmentDeliveryPolicy.RandomOnePerTeam, []);
        return new(AttachmentDeliveryPolicy.All, await db.Set<ChallengeAttachment>().AsNoTracking().Where(x => x.ChallengeId == scope.ChallengeId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new LiveSoloAttachmentView(x.Id, x.File.FileName, x.File.ContentType, x.File.ByteLength)).ToArrayAsync(ct));
    }
    public async Task<LiveSoloAttachmentCandidate?> SelectAsync(LiveSoloResourceRequest request, Guid? attachmentId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var scope = await AuthorizeAsync(request, ct); if (scope is null) return null;
                var random = await RandomAsync(scope.ChallengeId, ct);
                if (random == attachmentId.HasValue) return null;
                Guid selected;
                if (attachmentId is Guid requested) selected = requested;
                else
                {
                    var assigned = await db.LiveSoloAttachmentAssignments.SingleOrDefaultAsync(x => x.RoundQuestionId == request.QuestionId && x.TeamId == scope.TeamId, ct);
                    if (assigned is not null) selected = assigned.AttachmentId;
                    else
                    {
                        var candidates = await db.ChallengeFlags.AsNoTracking().Where(x => x.ChallengeId == scope.ChallengeId && x.DeletedAt == null
                            && x.SpecificationKind == SpecificationKind.Attachment && x.SpecificationId != null && x.MatchKind == ChallengeFlagMatchKind.Exact
                            && db.Set<ChallengeAttachment>().Any(a => a.Id == x.SpecificationId && a.ChallengeId == scope.ChallengeId && a.DeletedAt == null))
                            .Select(x => x.SpecificationId!.Value).Distinct().ToArrayAsync(ct);
                        if (candidates.Length == 0) return null;
                        var used = await db.LiveSoloAttachmentAssignments.Where(x => x.RoundQuestionId == request.QuestionId).Select(x => x.AttachmentId).ToArrayAsync(ct);
                        var unused = candidates.Where(x => !used.Contains(x)).ToArray(); var pool = unused.Length == 0 ? candidates : unused;
                        selected = pool[RandomNumberGenerator.GetInt32(pool.Length)];
                        db.LiveSoloAttachmentAssignments.Add(new() { RoundQuestionId = request.QuestionId, TeamId = scope.TeamId,
                            AttachmentId = selected, AssignedAt = clock.GetUtcNow() });
                    }
                }
                var attachment = await db.Set<ChallengeAttachment>().AsNoTracking().Where(x => x.Id == selected && x.ChallengeId == scope.ChallengeId && x.DeletedAt == null)
                    .Select(x => new { Metadata = new LiveSoloAttachmentView(x.Id, x.File.FileName, x.File.ContentType, x.File.ByteLength), x.FileId, x.File.ObjectKey }).SingleOrDefaultAsync(ct);
                if (attachment is null) return null;
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return new(attachment.Metadata, scope.TeamId, scope.CompetitionChallengeId, attachment.FileId, attachment.ObjectKey);
            }
            catch (Exception ex) when (attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex))) { db.ChangeTracker.Clear(); }
            catch (Exception ex) when (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)) { db.ChangeTracker.Clear(); return null; }
        }
    }
    public async Task<bool> RecordDownloadAsync(LiveSoloResourceRequest request, LiveSoloAttachmentCandidate selected, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var scope = await AuthorizeAsync(request, ct);
                if (scope is null || scope.TeamId != selected.TeamId || scope.CompetitionChallengeId != selected.CompetitionChallengeId
                    || !await db.Set<ChallengeAttachment>().AnyAsync(x => x.Id == selected.Metadata.Id && x.ChallengeId == scope.ChallengeId
                        && x.FileId == selected.FileId && x.DeletedAt == null, ct)) return false;
                if (await RandomAsync(scope.ChallengeId, ct) && !await db.LiveSoloAttachmentAssignments.AnyAsync(x =>
                    x.RoundQuestionId == request.QuestionId && x.TeamId == scope.TeamId && x.AttachmentId == selected.Metadata.Id, ct)) return false;
                var now = clock.GetUtcNow();
                var fact = new AttachmentDownloadGameplayFact { Id = Guid.CreateVersion7(now), CompetitionId = request.CompetitionId,
                    CompetitionChallengeId = scope.CompetitionChallengeId, TeamId = scope.TeamId, ActorUserId = request.ActorId,
                    ReferenceKind = GameplayFactReferenceKind.Attachment, ReferenceId = selected.Metadata.Id, State = GameplayFactState.Completed,
                    Result = GameplayFactResult.Applied, OccurredAt = now, UpdatedAt = now };
                db.GameplayFacts.Add(fact); db.LiveSoloDownloadEvidences.Add(new() { GameplayFactId = fact.Id, RoundQuestionId = request.QuestionId });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
            }
            catch (Exception ex) when (attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex))) { db.ChangeTracker.Clear(); }
            catch (Exception ex) when (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)) { db.ChangeTracker.Clear(); return false; }
        }
    }
}

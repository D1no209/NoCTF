using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Activities;

namespace NoCTF.Infrastructure.Theory.Questions;

public sealed class TheoryImageStore(NoCtfDbContext db) : ITheoryImageStore
{
    public async Task<TheoryImageResult> AttachAsync(Guid actorId, Guid questionId, Guid expectedRevisionId, Guid fileId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await CanEditAsync(actorId, questionId, ct)) return new(Failure: TheoryImageFailure.NotFoundOrForbidden);
        var question = await db.TheoryQuestions.SingleAsync(question => question.Id == questionId, ct);
        var previous = await db.TheoryQuestionRevisions.AsNoTracking().Include(revision => revision.Options).Include(revision => revision.Classifications).Include(revision => revision.Files)
            .SingleAsync(revision => revision.QuestionId == questionId && revision.Version == question.CurrentVersion, ct);
        if (previous.Id != expectedRevisionId) return new(Failure: TheoryImageFailure.Conflict);
        if (!await db.Files.AnyAsync(file => file.Id == fileId, ct)) return new(Failure: TheoryImageFailure.NotFoundOrForbidden);
        var revision = new TheoryQuestionRevision { Id = Guid.NewGuid(), QuestionId = questionId, Version = ++question.CurrentVersion,
            Title = previous.Title, Body = previous.Body, Explanation = previous.Explanation, Kind = previous.Kind, Difficulty = previous.Difficulty, CreatedAt = now,
            Options = previous.Options.Select(option => new TheoryQuestionOption { Id = Guid.NewGuid(), Order = option.Order, Content = option.Content,
                IsCorrect = option.IsCorrect, TruthValue = option.TruthValue }).ToList(),
            Classifications = previous.Classifications.Select(item => new TheoryQuestionClassification { Kind = item.Kind, Name = item.Name, NormalizedName = item.NormalizedName }).ToList(),
            Files = previous.Files.Select(file => new TheoryQuestionFile { FileId = file.FileId }).Append(new() { FileId = fileId }).ToList() };
        question.UpdatedAt = now; db.TheoryQuestionRevisions.Add(revision);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(fileId, revision.Id);
    }

    public async Task<TheoryImageReference?> ReadAsync(Guid actorId, Guid scopeId, Guid? paperId, Guid fileId, TheoryImageAudience audience, DateTimeOffset now, CancellationToken ct)
    {
        var permitted = false;
        if (audience == TheoryImageAudience.Editor)
            permitted = await CanEditAsync(actorId, scopeId, ct) && await db.TheoryQuestionRevisions.AnyAsync(revision => revision.QuestionId == scopeId && revision.Files.Any(file => file.FileId == fileId), ct);
        else if (paperId is { } id)
        {
            var access = await TheoryActivityAccess.ReadAsync(db, scopeId, actorId, now, ct);
            if (access is null) return null;
            var paper = await db.TheoryPapers.AsNoTracking().Include(paper => paper.Items).SingleOrDefaultAsync(paper => paper.Id == id, ct);
            if (paper is null) return null;
            var participant = await db.TheoryParticipants.AsNoTracking().SingleAsync(participant => participant.Id == paper.ParticipantId, ct);
            if (participant.ActivityId != scopeId) return null;
            permitted = audience == TheoryImageAudience.Reviewer ? access.CanJudge
                : access.CanParticipate && paper.State == TheoryPaperState.Active
                    && access.Activity.State == NoCTF.Domain.Theory.Activities.TheoryActivityState.Published
                    && now < NoCTF.Application.Theory.Activities.TheoryClock.Paper(paper.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now).At
                    && (access.TeamId is { } team ? participant.TeamId == team : participant.UserId == actorId);
            var versions = paper.Items.Select(item => item.RevisionId).ToArray();
            permitted = permitted && await db.TheoryQuestionRevisions.AnyAsync(revision => versions.Contains(revision.Id) && revision.Files.Any(file => file.FileId == fileId), ct);
        }
        return !permitted ? null : await db.Files.AsNoTracking().Where(file => file.Id == fileId)
            .Select(file => new TheoryImageReference(file.Id, file.ObjectKey, file.ContentType)).SingleOrDefaultAsync(ct);
    }

    private Task<bool> CanEditAsync(Guid actorId, Guid questionId, CancellationToken ct) => db.TheoryQuestions.AnyAsync(question => question.Id == questionId
        && db.Users.Any(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active
            && (user.Role == UserRole.Administrator || question.OwnerId == actorId
                || question.Grants.Any(grant => grant.UserId == actorId && grant.Permission >= TheoryQuestionPermission.Edit))), ct);
}

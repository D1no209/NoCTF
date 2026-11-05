using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Images;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Questionnaires.Activities;

namespace NoCTF.Infrastructure.Questionnaires;

public sealed class QuestionnaireImageStore(NoCtfDbContext db) : IQuestionnaireImageStore
{
    public async Task<Guid?> FindEditableRevisionAsync(Guid actorId, Guid challengeId, Guid questionId, CancellationToken ct)
    {
        if (!await CanEditAsync(actorId, challengeId, ct)) return null;
        return await db.Set<QuestionnaireCapabilityDefinition>().Where(capability => capability.ChallengeId == challengeId
                && capability.CurrentRevision.Questions.Any(question => question.Id == questionId))
            .Select(capability => (Guid?)capability.CurrentRevisionId).SingleOrDefaultAsync(ct);
    }

    public async Task<QuestionnaireImageResult> AttachAsync(Guid actorId, Guid challengeId, Guid questionId,
        Guid expectedRevisionId, Guid fileId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await CanEditAsync(actorId, challengeId, ct)) return new(Failure: QuestionnaireImageFailure.NotFoundOrForbidden);
        var challenge = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == challengeId, ct);
        var capability = challenge.Definition?.Questionnaire;
        if (capability is null || capability.CurrentRevisionId != expectedRevisionId) return new(Failure: QuestionnaireImageFailure.Conflict);
        if (!await db.Files.AnyAsync(file => file.Id == fileId, ct)) return new(Failure: QuestionnaireImageFailure.NotFoundOrForbidden);
        var copied = (QuestionnaireCapabilityDefinition)ChallengeCapabilityCopy.Copy(capability);
        copied.CurrentRevision.Number = checked(capability.CurrentRevision.Number + 1);
        copied.CurrentRevision.CreatedAt = now;
        var question = copied.CurrentRevision.Questions.SingleOrDefault(value => value.Id == questionId);
        if (question is null) return new(Failure: QuestionnaireImageFailure.NotFoundOrForbidden);
        question.Files.Add(new() { FileId = fileId });
        capability.CurrentRevision = copied.CurrentRevision;
        capability.CurrentRevisionId = copied.CurrentRevisionId;
        ChallengeDefinitionGraph.AssignChallengeId(challenge.Definition!, challengeId);
        challenge.UpdatedAt = now;
        db.Add(capability.CurrentRevision);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(fileId, capability.CurrentRevisionId);
    }

    public async Task<QuestionnaireImageReference?> ReadAsync(Guid actorId, Guid scopeId, Guid? sessionId, Guid fileId,
        QuestionnaireImageAudience audience, DateTimeOffset now, CancellationToken ct)
    {
        var permitted = false;
        if (audience == QuestionnaireImageAudience.Editor)
            permitted = await CanReadAsync(actorId, scopeId, ct) && await db.Set<QuestionnaireQuestion>()
                .AnyAsync(question => question.Revision.ChallengeId == scopeId && question.Files.Any(file => file.FileId == fileId), ct);
        else if (sessionId is { } id)
        {
            var access = await QuestionnaireActivityAccess.ReadAsync(db, scopeId, actorId, now, ct);
            if (access is null) return null;
            var paper = await db.QuestionnaireSessions.AsNoTracking().Include(value => value.Items).SingleOrDefaultAsync(value => value.Id == id, ct);
            if (paper is null) return null;
            var participant = await db.QuestionnaireParticipants.AsNoTracking().SingleAsync(value => value.Id == paper.ParticipantId, ct);
            if (participant.ActivityId != scopeId) return null;
            permitted = audience == QuestionnaireImageAudience.Reviewer ? access.CanJudge
                : access.CanParticipate && participant.UserId == actorId && paper.State == QuestionnaireSessionState.Active
                    && access.Activity.State == NoCTF.Domain.Questionnaires.Activities.QuestionnaireActivityState.Published
                    && now < NoCTF.Application.Questionnaires.Activities.QuestionnaireClock.Session(paper.OpenedAt, access.Activity.DurationSeconds, access.Deadline, access.Pauses, now).At;
            var versions = paper.Items.Select(item => item.RevisionId).ToArray();
            permitted = permitted && await db.Set<QuestionnaireQuestion>()
                .AnyAsync(question => versions.Contains(question.VersionId) && question.Files.Any(file => file.FileId == fileId), ct);
        }
        return !permitted ? null : await db.Files.AsNoTracking().Where(file => file.Id == fileId)
            .Select(file => new QuestionnaireImageReference(file.Id, file.ObjectKey, file.ContentType)).SingleOrDefaultAsync(ct);
    }

    private Task<bool> CanEditAsync(Guid actorId, Guid challengeId, CancellationToken ct) =>
        db.Challenges.AnyAsync(challenge => challenge.Id == challengeId && challenge.CatalogScope == ChallengeCatalogScope.QuestionBank
            && db.Users.Any(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active
                && (user.Role == UserRole.Administrator || challenge.OwnerId == actorId
                    || challenge.Managers.Any(manager => manager.UserId == actorId))), ct);

    private Task<bool> CanReadAsync(Guid actorId, Guid challengeId, CancellationToken ct) =>
        db.Challenges.AnyAsync(challenge => challenge.Id == challengeId && challenge.CatalogScope == ChallengeCatalogScope.QuestionBank
            && db.Users.Any(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active
                && (user.Role == UserRole.Administrator || user.Role == UserRole.Organizer
                    && (challenge.Visibility == ChallengeVisibility.Shared || challenge.OwnerId == actorId
                        || challenge.Managers.Any(manager => manager.UserId == actorId)))), ct);
}

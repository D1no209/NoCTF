using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Questions;

public sealed class TheoryQuestionBank(NoCtfDbContext db,
    NoCTF.Application.Messaging.IPostCommitMessagePublisher? navigationChanges = null) : ITheoryQuestionBank
{
    public Task<TheoryBankWriteResult> SaveAsync(Guid actorId, Guid? questionId, TheoryQuestionRevision revision, CancellationToken ct)
        => MutateAsync(() => SaveCoreAsync(actorId, questionId, revision, ct), new TheoryBankWriteResult(Failure: TheoryBankFailure.Conflict));

    private async Task<TheoryBankWriteResult> SaveCoreAsync(Guid actorId, Guid? questionId,
        TheoryQuestionRevision revision, CancellationToken ct)
    {
        if (TheoryQuestionPolicy.Validate(revision) is { } invalid)
            return new(Failure: TheoryBankFailure.InvalidContent, Validation: invalid);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await ActorAsync(actorId, ct);
        if (actor is null) return new(Failure: TheoryBankFailure.NotFoundOrForbidden);
        var files = revision.Files.Select(file => file.FileId).ToArray();
        if (files.Length > 0)
        {
            var readableIds = Authorized(actor, TheoryQuestionPermission.Edit).Select(item => item.Id);
            var referenced = await db.TheoryQuestionRevisions.Where(item => readableIds.Contains(item.QuestionId))
                .SelectMany(item => item.Files).Where(file => files.Contains(file.FileId))
                .Select(file => file.FileId).Distinct().CountAsync(ct);
            if (referenced != files.Length) return new(Failure: TheoryBankFailure.FileNotAuthorized);
        }
        TheoryQuestion question;
        if (questionId is { } existingId)
        {
            var existing = await Authorized(actor, TheoryQuestionPermission.Edit).SingleOrDefaultAsync(item => item.Id == existingId, ct);
            if (existing is null) return new(Failure: TheoryBankFailure.NotFoundOrForbidden);
            question = existing;
        }
        else
        {
            if (!actor.Role.CanManageResources()) return new(Failure: TheoryBankFailure.CreationForbidden);
            var source = new TheoryQuestionSource { Id = Guid.NewGuid() };
            db.Add(source);
            question = new() { Id = Guid.CreateVersion7(revision.CreatedAt), OwnerId = actorId, SourceId = source.Id, CreatedAt = revision.CreatedAt };
            db.TheoryQuestions.Add(question);
        }

        question.CurrentVersion++;
        question.UpdatedAt = revision.CreatedAt;
        revision.QuestionId = question.Id;
        revision.Version = question.CurrentVersion;
        db.TheoryQuestionRevisions.Add(revision);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(new(question.Id, revision.Id, revision.Version, question.SourceId));
    }

    public Task<TheoryBankWriteResult> CopyAsync(Guid actorId, Guid questionId, DateTimeOffset now, CancellationToken ct)
        => MutateAsync(() => CopyCoreAsync(actorId, questionId, now, ct), new TheoryBankWriteResult(Failure: TheoryBankFailure.Conflict));

    private async Task<TheoryBankWriteResult> CopyCoreAsync(Guid actorId, Guid questionId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await ActorAsync(actorId, ct);
        if (actor is null || !actor.Role.CanManageResources()) return new(Failure: TheoryBankFailure.CreationForbidden);
        var source = await Authorized(actor, TheoryQuestionPermission.Edit).AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == questionId, ct);
        if (source is null) return new(Failure: TheoryBankFailure.NotFoundOrForbidden);
        var original = await Revisions().SingleAsync(item => item.QuestionId == source.Id && item.Version == source.CurrentVersion, ct);
        var copy = new TheoryQuestion { Id = Guid.CreateVersion7(now), SourceId = source.SourceId,
            OwnerId = actorId, CurrentVersion = 1, CreatedAt = now, UpdatedAt = now };
        var revision = new TheoryQuestionRevision { Id = Guid.NewGuid(), QuestionId = copy.Id, Version = 1,
            Title = original.Title, Body = original.Body, Explanation = original.Explanation, Kind = original.Kind,
            Difficulty = original.Difficulty, CreatedAt = now,
            Options = original.Options.Select(item => new TheoryQuestionOption { Id = Guid.NewGuid(), Order = item.Order,
                Content = item.Content, IsCorrect = item.IsCorrect, TruthValue = item.TruthValue }).ToList(),
            Classifications = original.Classifications.Select(item => new TheoryQuestionClassification
                { Kind = item.Kind, Name = item.Name, NormalizedName = item.NormalizedName }).ToList(),
            Files = original.Files.Select(item => new TheoryQuestionFile { FileId = item.FileId }).ToList() };
        copy.Revisions.Add(revision);
        db.TheoryQuestions.Add(copy);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(new(copy.Id, revision.Id, revision.Version, copy.SourceId));
    }

    public async Task<TheoryQuestionPage> ListAsync(Guid actorId, string? search, TheoryQuestionKind? kind,
        int offset, int limit, CancellationToken ct, TheoryQuestionFilter? filters = null)
    {
        var actor = await ActorAsync(actorId, ct);
        if (actor is null) return new([], 0);
        var query = Authorized(actor, TheoryQuestionPermission.Use).AsNoTracking();
        var keyword = search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(keyword)) query = query.Where(question => question.Revisions
            .Any(revision => revision.Version == question.CurrentVersion && revision.Title.ToUpper().Contains(keyword)));
        if (kind is { } filter) query = query.Where(question => question.Revisions
            .Any(revision => revision.Version == question.CurrentVersion && revision.Kind == filter));
        if (filters?.State is { } state) query = query.Where(question => question.State == state);
        if (filters?.Difficulty is { } difficulty) query = query.Where(question => question.Revisions.Any(revision => revision.Version == question.CurrentVersion && revision.Difficulty == difficulty));
        if (!string.IsNullOrWhiteSpace(filters?.Knowledge))
        {
            var knowledge = TheoryQuestionPolicy.Normalize(filters.Knowledge);
            query = query.Where(question => question.Revisions.Any(revision => revision.Version == question.CurrentVersion
                && revision.Classifications.Any(item => item.Kind == TheoryClassificationKind.Knowledge && item.NormalizedName == knowledge)));
        }
        if (!string.IsNullOrWhiteSpace(filters?.Tag))
        {
            var tag = TheoryQuestionPolicy.Normalize(filters.Tag);
            query = query.Where(question => question.Revisions.Any(revision => revision.Version == question.CurrentVersion
                && revision.Classifications.Any(item => item.Kind == TheoryClassificationKind.Tag && item.NormalizedName == tag)));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(question => question.UpdatedAt).ThenBy(question => question.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200))
            .Select(question => new TheoryQuestionSummary(question.Id, question.OwnerId, question.SourceId, question.CurrentVersion,
                question.Revisions.Where(revision => revision.Version == question.CurrentVersion).Select(revision => revision.Title).Single(),
                question.Revisions.Where(revision => revision.Version == question.CurrentVersion).Select(revision => revision.Kind).Single(),
                question.Revisions.Where(revision => revision.Version == question.CurrentVersion).Select(revision => revision.Difficulty).Single(),
                question.State, question.UpdatedAt,
                actor.Role == UserRole.Administrator || question.OwnerId == actor.Id
                    || question.Grants.Any(grant => grant.UserId == actor.Id && grant.Permission >= TheoryQuestionPermission.Edit),
                actor.Role == UserRole.Administrator || question.OwnerId == actor.Id
                    || question.Grants.Any(grant => grant.UserId == actor.Id && grant.Permission == TheoryQuestionPermission.Manage))
                { Classifications = question.Revisions.Where(revision => revision.Version == question.CurrentVersion).SelectMany(revision => revision.Classifications)
                    .Select(item => new TheoryClassificationInput(item.Kind, item.Name)).ToArray(),
                  PaperReferences = db.Set<NoCTF.Domain.Theory.Papers.TheoryPaperItem>().Count(item => question.Revisions.Any(revision => revision.Id == item.RevisionId)),
                  FirstExposedAt = db.Set<TheoryQuestionSource>().Where(source => source.Id == question.SourceId).Select(source => source.FirstExposedAt).Single() })
            .ToArrayAsync(ct);
        return new(page, total);
    }

    public async Task<TheoryQuestionContent?> ReadAsync(Guid actorId, Guid questionId, CancellationToken ct)
    {
        var actor = await ActorAsync(actorId, ct);
        if (actor is null) return null;
        var question = await Authorized(actor, TheoryQuestionPermission.Edit).AsNoTracking().Include(item => item.Grants)
            .SingleOrDefaultAsync(item => item.Id == questionId, ct);
        if (question is null) return null;
        var revision = await Revisions().SingleAsync(item => item.QuestionId == question.Id && item.Version == question.CurrentVersion, ct);
        var canManage = TheoryQuestionPolicy.Allows(question, actorId, actor.Role == UserRole.Administrator, TheoryQuestionPermission.Manage);
        var grantIds = question.Grants.Select(grant => grant.UserId).ToArray();
        var names = canManage ? await db.Users.AsNoTracking().Where(user => grantIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, user => user.UserName, ct) : [];
        return new(new(question.Id, question.OwnerId, question.SourceId, question.CurrentVersion, revision.Title, revision.Kind,
            revision.Difficulty, question.State, question.UpdatedAt, true,
            TheoryQuestionPolicy.Allows(question, actorId, actor.Role == UserRole.Administrator, TheoryQuestionPermission.Manage)),
            revision.Id, new(revision.Title, revision.Body, revision.Explanation, revision.Kind, revision.Difficulty,
                revision.Options.OrderBy(option => option.Order).Select(option => new TheoryOptionInput(option.Content, option.IsCorrect, option.TruthValue)).ToArray(),
                revision.Classifications.Select(item => new TheoryClassificationInput(item.Kind, item.Name)).ToArray(),
                revision.Files.Select(file => file.FileId).ToArray()))
        { Grants = canManage ? question.Grants.Select(grant => new TheoryQuestionAccessGrant(grant.UserId, names.GetValueOrDefault(grant.UserId, grant.UserId.ToString()), grant.Permission)).ToArray() : [] };
    }

    public Task<TheoryBankFailure?> SetStateAsync(Guid actorId, Guid questionId, TheoryQuestionState state,
        DateTimeOffset now, CancellationToken ct)
        => MutateAsync<TheoryBankFailure?>(() => SetStateCoreAsync(actorId, questionId, state, now, ct), TheoryBankFailure.Conflict);

    private async Task<TheoryBankFailure?> SetStateCoreAsync(Guid actorId, Guid questionId, TheoryQuestionState state,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!Enum.IsDefined(state)) return TheoryBankFailure.InvalidContent;
        var actor = await ActorAsync(actorId, ct);
        if (actor is null) return TheoryBankFailure.NotFoundOrForbidden;
        var question = await Authorized(actor, TheoryQuestionPermission.Edit).SingleOrDefaultAsync(item => item.Id == questionId, ct);
        if (question is null) return TheoryBankFailure.NotFoundOrForbidden;
        question.State = state;
        question.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public Task<TheoryBankFailure?> GrantAsync(Guid actorId, Guid questionId, Guid userId,
        TheoryQuestionPermission? permission, CancellationToken ct)
        => MutateAsync<TheoryBankFailure?>(() => GrantCoreAsync(actorId, questionId, userId, permission, ct), TheoryBankFailure.Conflict);

    private async Task<TheoryBankFailure?> GrantCoreAsync(Guid actorId, Guid questionId, Guid userId,
        TheoryQuestionPermission? permission, CancellationToken ct)
    {
        if (permission is { } value && !Enum.IsDefined(value)) return TheoryBankFailure.InvalidPermission;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var actor = await ActorAsync(actorId, ct);
        if (actor is null) return TheoryBankFailure.NotFoundOrForbidden;
        var question = await Authorized(actor, TheoryQuestionPermission.Manage).Include(item => item.Grants)
            .SingleOrDefaultAsync(item => item.Id == questionId, ct);
        if (question is null) return TheoryBankFailure.NotFoundOrForbidden;
        if (userId == question.OwnerId || await ActorAsync(userId, ct) is null) return TheoryBankFailure.InvalidPermission;
        var grant = question.Grants.SingleOrDefault(item => item.UserId == userId);
        if (permission is null)
        {
            if (grant is not null) db.Remove(grant);
        }
        else if (grant is null) question.Grants.Add(new() { UserId = userId, Permission = permission.Value });
        else grant.Permission = permission.Value;
        db.Entry(question).Property(item => item.ConcurrencyStamp).IsModified = true;
        if (navigationChanges is not null)
            await navigationChanges.PublishAsync(new NoCTF.Application.Messaging.NavigationCapabilitiesChanged([userId]));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (navigationChanges is not null) await navigationChanges.FlushCommittedMessagesAsync();
        return null;
    }

    private Task<User?> ActorAsync(Guid actorId, CancellationToken ct) => db.Users.AsNoTracking()
        .SingleOrDefaultAsync(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active, ct);

    private async Task<T> MutateAsync<T>(Func<Task<T>> mutation, T conflict)
    {
        try { return await mutation(); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
        {
            navigationChanges?.DiscardPendingMessages();
            db.ChangeTracker.Clear();
            return conflict;
        }
    }

    private IQueryable<TheoryQuestion> Authorized(User actor, TheoryQuestionPermission permission) => db.TheoryQuestions
        .Where(question => actor.Role == UserRole.Administrator || question.OwnerId == actor.Id
            || question.Grants.Any(grant => grant.UserId == actor.Id && grant.Permission >= permission));

    private IQueryable<TheoryQuestionRevision> Revisions() => db.TheoryQuestionRevisions.AsNoTracking()
        .Include(revision => revision.Options).Include(revision => revision.Classifications).Include(revision => revision.Files);
}

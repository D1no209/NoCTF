using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Theory.Activities;

public sealed class TheoryActivityStore(NoCtfDbContext db, IPostCommitMessagePublisher? publisher = null, ILeaderboardCache? leaderboard = null,
    NoCTF.Application.Theory.Realtime.ITheoryChangePublisher? signals = null, TimeProvider? timeProvider = null) : ITheoryActivityStore
{
    public Task<TheoryActivityResult> SaveAsync(TheoryActivityCommand command, CancellationToken ct) => MutateAsync(() => SaveCoreAsync(command, ct));
    private async Task<TheoryActivityResult> SaveCoreAsync(TheoryActivityCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == command.ActorId && user.AccountStatus == UserAccountStatus.Active, ct);
        if (user is null) return new(Failure: TheoryActivityError.NotFoundOrForbidden);
        var competition = command.CompetitionId is not { } competitionId ? null : await db.Competitions.AsNoTracking()
            .Include(item => item.Collaborators).SingleOrDefaultAsync(item => item.Id == competitionId && item.DeletedAt == null, ct);
        if (command.CompetitionId is not null ? competition is null || user.Role != UserRole.Administrator
            && competition.OwnerId != user.Id && !competition.Collaborators.Any(member => member.UserId == user.Id && member.Role == CompetitionCollaboratorRole.Manager)
            : !user.Role.CanManageResources()) return new(Failure: TheoryActivityError.NotFoundOrForbidden);
        var desired = TheoryActivityMapping.Build(command);
        if (TheoryActivityPolicy.Validate(desired, competition?.Mode) is { } invalid)
            return new(Failure: TheoryActivityError.InvalidConfiguration, Validation: invalid);
        var linked = command.CompetitionId is null ? null : await db.CompetitionTheoryActivities.SingleOrDefaultAsync(link => link.CompetitionId == command.CompetitionId, ct);
        if (linked is not null && linked.ActivityId != command.ActivityId) return new(Failure: TheoryActivityError.AlreadyAssociated);
        if (command.ActivityId is { } existingId)
        {
            var context = await TheoryActivityAccess.ReadAsync(db, existingId, command.ActorId, command.Now, ct);
            if (context is null || !context.CanManage || context.Competition?.Id != command.CompetitionId)
                return new(Failure: TheoryActivityError.NotFoundOrForbidden);
            var existing = context.Activity;
            if (existing.State != TheoryActivityState.Draft) return new(Failure: TheoryActivityError.PublishedConfigurationLocked);
            db.RemoveRange(existing.Rules);
            // Release unique rule ordering before inserting replacement rows, within the same transaction.
            await db.SaveChangesAsync(ct);
            existing.Name = desired.Name; existing.Kind = desired.Kind; existing.OpensAt = desired.OpensAt;
            existing.ClosesAt = desired.ClosesAt; existing.DurationSeconds = desired.DurationSeconds;
            existing.AttemptLimit = desired.AttemptLimit; existing.ScoringMode = desired.ScoringMode;
            existing.AllowExposedQuestions = desired.AllowExposedQuestions; existing.Rules = desired.Rules;
            foreach (var rule in existing.Rules) { rule.ActivityId = existing.Id; db.Add(rule); }
            desired = existing;
        }
        else
        {
            db.TheoryActivities.Add(desired);
            if (competition is not null) db.CompetitionTheoryActivities.Add(new() { CompetitionId = competition.Id, ActivityId = desired.Id });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(TheoryActivityMapping.Summary(desired, competition?.Id));
    }

    public Task<TheoryActivityResult> TransitionAsync(TheoryActivityTransition command, CancellationToken ct) => MutateAsync(() => TransitionCoreAsync(command, ct));
    private async Task<TheoryActivityResult> TransitionCoreAsync(TheoryActivityTransition command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await TheoryActivityAccess.ReadAsync(db, command.ActivityId, command.ActorId, command.Now, ct, includeCandidates: true);
        if (context is null || !context.CanManage) return new(Failure: TheoryActivityError.NotFoundOrForbidden);
        var activity = context.Activity;
        var operation = TheoryActivityOperationKind.Published;
        if (command.Action == TheoryActivityAction.Publish)
        {
            if (activity.State != TheoryActivityState.Draft || command.Now >= activity.ClosesAt || context.Competition?.Status == CompetitionStatus.Finished)
                return new(Failure: TheoryActivityError.InvalidTransition);
            var failure = await FreezeCandidatesAsync(activity, command.ActorId, command.Now, ct);
            if (failure is not null) return failure;
            var capacity = TheoryPaperAllocator.Allocate(activity.Rules, new HashSet<Guid>(), activity.AttemptLimit ?? 1, ct);
            if (!capacity.Succeeded) return new(Failure: TheoryActivityError.InsufficientQuestions, Shortages: capacity.Shortages);
            activity.State = TheoryActivityState.Published; activity.PublishedAt = command.Now; activity.IssuanceStopped = false;
            var link = await db.CompetitionTheoryActivities.SingleOrDefaultAsync(link => link.ActivityId == activity.Id, ct);
            if (link is not null) link.Enabled = true;
            if (activity.AllowExposedQuestions) db.Add(Audit(command, TheoryActivityOperationKind.ExposureAccepted));
        }
        else if (command.Action == TheoryActivityAction.Withdraw)
        {
            if (activity.State != TheoryActivityState.Published) return new(Failure: TheoryActivityError.InvalidTransition);
            if (await db.TheoryParticipants.AnyAsync(item => item.ActivityId == activity.Id && item.IssuedCount > 0, ct))
                return new(Failure: TheoryActivityError.PapersAlreadyIssued);
            activity.State = TheoryActivityState.Draft; activity.PublishedAt = null;
            foreach (var pause in activity.Pauses.Where(pause => pause.EndedAt is null)) pause.EndedAt = command.Now;
            foreach (var rule in activity.Rules) { db.RemoveRange(rule.Candidates); rule.Candidates.Clear(); }
            operation = TheoryActivityOperationKind.Withdrawn;
        }
        else
        {
            if (activity.State != TheoryActivityState.Published) return new(Failure: TheoryActivityError.InvalidTransition);
            switch (command.Action)
            {
                case TheoryActivityAction.Pause:
                    if (command.Now >= context.Deadline.At || activity.Pauses.Any(pause => pause.EndedAt is null)) return new(Failure: TheoryActivityError.InvalidTransition);
                    var newPause = new TheoryActivityPause { Id = Guid.NewGuid(), ActivityId = activity.Id, StartedAt = command.Now };
                    activity.Pauses.Add(newPause); db.Add(newPause);
                    operation = TheoryActivityOperationKind.Paused;
                    break;
                case TheoryActivityAction.Resume:
                    var pause = activity.Pauses.SingleOrDefault(pause => pause.EndedAt is null);
                    if (pause is null) return new(Failure: TheoryActivityError.InvalidTransition);
                    pause.EndedAt = command.Now; operation = TheoryActivityOperationKind.Resumed;
                    break;
                case TheoryActivityAction.StopIssuance:
                    activity.IssuanceStopped = true; operation = TheoryActivityOperationKind.IssuanceStopped;
                    break;
                case TheoryActivityAction.ExtendDeadline:
                    if (command.Now >= context.Deadline.At || command.Deadline is not { } deadline || deadline <= activity.ClosesAt)
                        return new(Failure: TheoryActivityError.DeadlineExpired);
                    activity.ClosesAt = deadline; operation = TheoryActivityOperationKind.DeadlineExtended;
                    break;
                case TheoryActivityAction.Finish:
                case TheoryActivityAction.Void:
                    if (string.IsNullOrWhiteSpace(command.Reason)) return new(Failure: TheoryActivityError.InvalidConfiguration);
                    activity.State = command.Action == TheoryActivityAction.Finish ? TheoryActivityState.Finished : TheoryActivityState.Voided;
                    activity.FinishedAt = command.Now; activity.IssuanceStopped = true;
                    operation = command.Action == TheoryActivityAction.Finish ? TheoryActivityOperationKind.Finished : TheoryActivityOperationKind.Voided;
                    break;
                default: return new(Failure: TheoryActivityError.InvalidTransition);
            }
        }
        db.Entry(activity).Property(item => item.ConcurrencyStamp).IsModified = true;
        db.Add(Audit(command, operation));
        if (command.Action == TheoryActivityAction.Void && context.Competition is not null)
        {
            var stamp = Guid.NewGuid();
            await db.GameplayFacts.Where(fact => fact.CompetitionId == context.Competition.Id && fact.Kind == GameplayFactKind.TheoryPaperSubmission)
                .ExecuteUpdateAsync(setters => setters.SetProperty(fact => fact.State, GameplayFactState.Queued)
                    .SetProperty(fact => fact.UpdatedAt, command.Now).SetProperty(fact => fact.ConcurrencyStamp, stamp), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (context.Competition is not null)
        {
            if (leaderboard is not null) await leaderboard.InvalidateAsync(context.Competition.Id, ct);
            if (publisher is not null) { await publisher.PublishAsync(new ProjectLeaderboard(context.Competition.Id)); await publisher.FlushCommittedMessagesAsync(); }
        }
        if (signals is not null) await signals.PublishAsync(new(activity.Id, null), ct);
        return new(TheoryActivityMapping.Summary(activity, context.Competition?.Id));
    }

    private async Task<TheoryActivityResult?> FreezeCandidatesAsync(TheoryActivity activity, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var isAdmin = await db.Users.AnyAsync(user => user.Id == actorId && user.Role == UserRole.Administrator, ct);
        var bank = db.TheoryQuestions.AsNoTracking().Where(question => question.State == TheoryQuestionState.Available
            && (isAdmin || question.OwnerId == actorId || question.Grants.Any(grant => grant.UserId == actorId)));
        var available = await bank.SelectMany(question => question.Revisions.Where(revision => revision.Version == question.CurrentVersion)
            .Select(revision => new { revision.Id, question.SourceId, revision.Kind, revision.Difficulty, revision.Classifications })).ToArrayAsync(ct);
        var exposed = await db.Set<TheoryQuestionExposure>().Select(exposure => exposure.SourceId)
            .Union(db.Set<TheoryQuestionSource>().Where(source => source.FirstExposedAt != null).Select(source => source.Id)).ToArrayAsync(ct);
        var reserved = activity.Kind == TheoryActivityKind.Competition ? []
            : await TheoryReservations.ReadAsync(db, available.Select(item => item.SourceId).Distinct().ToArray(), activity.Id, now, ct);
        foreach (var rule in activity.Rules)
        {
            rule.Candidates = available.Where(candidate => candidate.Kind == rule.Kind && (rule.Difficulty is null || candidate.Difficulty == rule.Difficulty)
                && rule.Filters.All(filter => candidate.Classifications.Any(item => item.Kind == filter.Kind && item.NormalizedName == filter.NormalizedName))
                && (activity.Kind != TheoryActivityKind.Competition || activity.AllowExposedQuestions || !exposed.Contains(candidate.SourceId))
                && (activity.Kind == TheoryActivityKind.Competition || !reserved.Contains(candidate.SourceId)))
                .Select(candidate => new TheoryDrawCandidate { RuleId = rule.Id, RevisionId = candidate.Id, SourceId = candidate.SourceId }).ToList();
        }
        return null;
    }

    public async Task<TheoryActivityCheck?> CheckAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct, includeCandidates: true);
        if (access is null || !access.CanManage) return null;
        if (access.Activity.State == TheoryActivityState.Draft) await FreezeCandidatesAsync(access.Activity, actorId, now, ct);
        var result = TheoryPaperAllocator.Allocate(access.Activity.Rules, new HashSet<Guid>(), access.Activity.AttemptLimit ?? 1, ct);
        var check = new TheoryActivityCheck(result.Succeeded, access.Activity.Rules.OrderBy(rule => rule.Order)
            .Select(rule => new TheoryRuleCapacity(rule.Order, checked(rule.Count * (access.Activity.AttemptLimit ?? 1)),
                rule.Candidates.Select(candidate => candidate.SourceId).Distinct().Count())).ToArray(), result.Shortages);
        db.ChangeTracker.Clear();
        return check;
    }

    public async Task<TheoryActivityPage> ListAsync(Guid actorId, bool staff, Guid? competitionId, CancellationToken ct, int offset = 0, int limit = 25, string? keyword = null)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active, ct);
        if (user is null) return new([], 0);
        var query = db.TheoryActivities.AsNoTracking().Include(activity => activity.Rules).AsQueryable();
        if (competitionId is { } id) query = query.Where(activity => db.CompetitionTheoryActivities.Any(link => link.CompetitionId == id && link.ActivityId == activity.Id));
        else query = query.Where(activity => activity.Kind != TheoryActivityKind.Competition);
        if (staff && competitionId is null) query = query.Where(activity => user.Role == UserRole.Administrator || activity.OwnerId == actorId);
        if (!staff) query = query.Where(activity => activity.State != TheoryActivityState.Draft && activity.State != TheoryActivityState.Voided);
        if (!string.IsNullOrWhiteSpace(keyword)) { var search = keyword.Trim().ToUpperInvariant(); query = query.Where(activity => activity.Name.ToUpper().Contains(search)); }
        if (competitionId is not null)
        {
            var activityId = await query.Select(activity => (Guid?)activity.Id).FirstOrDefaultAsync(ct);
            var scope = activityId is null ? null : await TheoryActivityAccess.ReadAsync(db, activityId.Value, actorId, (timeProvider ?? TimeProvider.System).GetUtcNow(), ct);
            if (scope is null || (staff ? !scope.CanObserve : !scope.CanParticipate)) return new([], 0);
        }
        var total = await query.CountAsync(ct);
        var activities = await query.OrderByDescending(activity => activity.CreatedAt).ThenBy(activity => activity.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArrayAsync(ct);
        var result = new List<TheoryActivitySummary>();
        foreach (var activity in activities)
        {
            if (competitionId is not null)
            {
                var access = await TheoryActivityAccess.ReadAsync(db, activity.Id, actorId, (timeProvider ?? TimeProvider.System).GetUtcNow(), ct);
                if (access is null || (staff ? !access.CanObserve : !access.CanParticipate)) continue;
            }
            result.Add(TheoryActivityMapping.Summary(activity, competitionId));
        }
        return new(result, total);
    }

    public async Task<TheoryActivityManagement?> ReadManagementAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await TheoryActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var issued = await db.TheoryParticipants.Where(participant => participant.ActivityId == activityId).SumAsync(participant => participant.IssuedCount, ct);
        var operations = new List<TheoryActivityAction>();
        if (access.CanManage)
        {
            if (access.Activity.State == TheoryActivityState.Draft && now < access.Activity.ClosesAt && access.Competition?.Status != CompetitionStatus.Finished) operations.Add(TheoryActivityAction.Publish);
            if (access.Activity.State == TheoryActivityState.Published)
            {
                if (issued == 0) operations.Add(TheoryActivityAction.Withdraw);
                if (now < access.Deadline.At)
                {
                    operations.Add(access.Activity.Pauses.Any(pause => pause.EndedAt is null) ? TheoryActivityAction.Resume : TheoryActivityAction.Pause);
                    operations.Add(TheoryActivityAction.ExtendDeadline);
                }
                if (!access.Activity.IssuanceStopped) operations.Add(TheoryActivityAction.StopIssuance);
                operations.Add(TheoryActivityAction.Finish); operations.Add(TheoryActivityAction.Void);
            }
        }
        return new(TheoryActivityMapping.Summary(access.Activity, access.Competition?.Id), access.Activity.Rules.OrderBy(rule => rule.Order)
            .Select(rule => new TheoryRuleInput(rule.Kind, rule.Difficulty, rule.Count, rule.Points,
                rule.Filters.Select(filter => new TheoryClassificationFilter(filter.Kind, filter.NormalizedName)).ToArray())).ToArray(),
            access.Activity.AllowExposedQuestions, access.CanManage, access.CanJudge, access.Deadline)
        {
            Corrections = await db.Set<TheoryGradingOverride>().AsNoTracking().Where(rule => rule.ActivityId == activityId)
                .Select(rule => new TheoryCorrectionProgress(rule.RevisionId, rule.TargetRevision, rule.AppliedRevision == rule.TargetRevision)).ToArrayAsync(ct),
            IssuedCount = issued, AllowedOperations = operations, CompetitionEndAt = access.Competition?.EndAt
        };
    }

    private static TheoryActivityOperation Audit(TheoryActivityTransition command, TheoryActivityOperationKind kind) => new()
    { Id = Guid.NewGuid(), ActivityId = command.ActivityId, ActorId = command.ActorId, OccurredAt = command.Now, Kind = kind, Reason = command.Reason };

    private async Task<TheoryActivityResult> MutateAsync(Func<Task<TheoryActivityResult>> operation)
    {
        try { return await operation(); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
        { db.ChangeTracker.Clear(); return new(Failure: TheoryActivityError.Conflict); }
    }
}

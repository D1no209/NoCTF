using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Questionnaires;

namespace NoCTF.Infrastructure.Questionnaires.Activities;

public sealed class QuestionnaireActivityStore(NoCtfDbContext db,
    NoCTF.Application.Questionnaires.Realtime.IQuestionnaireChangePublisher? signals = null) : IQuestionnaireActivityStore
{
    public Task<QuestionnaireActivityResult> SaveAsync(QuestionnaireActivityCommand command, CancellationToken ct) => MutateAsync(() => SaveCoreAsync(command, ct));
    private async Task<QuestionnaireActivityResult> SaveCoreAsync(QuestionnaireActivityCommand command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == command.ActorId && user.AccountStatus == UserAccountStatus.Active, ct);
        if (user is null || !user.Role.CanManageResources()) return new(Failure: QuestionnaireActivityError.NotFoundOrForbidden);
        var desired = QuestionnaireActivityMapping.Build(command);
        if (QuestionnaireActivityPolicy.Validate(desired) is { } invalid)
            return new(Failure: QuestionnaireActivityError.InvalidConfiguration, Validation: invalid);
        if (command.ActivityId is { } existingId)
        {
            var context = await QuestionnaireActivityAccess.ReadAsync(db, existingId, command.ActorId, command.Now, ct);
            if (context is null || !context.CanManage) return new(Failure: QuestionnaireActivityError.NotFoundOrForbidden);
            var existing = context.Activity;
            if (existing.State != QuestionnaireActivityState.Draft) return new(Failure: QuestionnaireActivityError.PublishedConfigurationLocked);
            db.RemoveRange(existing.Rules); db.RemoveRange(existing.FixedQuestionnaires);
            await db.SaveChangesAsync(ct);
            existing.Name = desired.Name; existing.Kind = desired.Kind; existing.OpensAt = desired.OpensAt;
            existing.ClosesAt = desired.ClosesAt; existing.DurationSeconds = desired.DurationSeconds;
            existing.AttemptLimit = desired.AttemptLimit; existing.ScoringMode = desired.ScoringMode;
            existing.AllowExposedQuestions = desired.AllowExposedQuestions; existing.Rules = desired.Rules;
            existing.SelectionKind = desired.SelectionKind; existing.FixedQuestionnaires = desired.FixedQuestionnaires;
            foreach (var rule in existing.Rules) { rule.ActivityId = existing.Id; db.Add(rule); }
            desired = existing;
        }
        else db.QuestionnaireActivities.Add(desired);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(QuestionnaireActivityMapping.Summary(desired));
    }

    public Task<QuestionnaireActivityResult> TransitionAsync(QuestionnaireActivityTransition command, CancellationToken ct) => MutateAsync(() => TransitionCoreAsync(command, ct));
    private async Task<QuestionnaireActivityResult> TransitionCoreAsync(QuestionnaireActivityTransition command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var context = await QuestionnaireActivityAccess.ReadAsync(db, command.ActivityId, command.ActorId, command.Now, ct, includeCandidates: true);
        if (context is null || !context.CanManage) return new(Failure: QuestionnaireActivityError.NotFoundOrForbidden);
        var activity = context.Activity;
        var operation = QuestionnaireActivityOperationKind.Published;
        if (command.Action == QuestionnaireActivityAction.Publish)
        {
            if (activity.State != QuestionnaireActivityState.Draft || command.Now >= activity.ClosesAt)
                return new(Failure: QuestionnaireActivityError.InvalidTransition);
            var failure = await FreezeCandidatesAsync(activity, command.ActorId, command.Now, ct);
            if (failure is not null) return failure;
            var capacity = QuestionnaireSessionAllocator.Allocate(activity.Rules, new HashSet<Guid>(),
                activity.SelectionKind == QuestionnaireSelectionKind.Fixed ? 1 : activity.AttemptLimit ?? 1, ct);
            if (!capacity.Succeeded) return new(Failure: QuestionnaireActivityError.InsufficientQuestions, Shortages: capacity.Shortages);
            activity.State = QuestionnaireActivityState.Published; activity.PublishedAt = command.Now; activity.IssuanceStopped = false;
            if (activity.AllowExposedQuestions) db.Add(Audit(command, QuestionnaireActivityOperationKind.ExposureAccepted));
        }
        else if (command.Action == QuestionnaireActivityAction.Withdraw)
        {
            if (activity.State != QuestionnaireActivityState.Published) return new(Failure: QuestionnaireActivityError.InvalidTransition);
            if (await db.QuestionnaireParticipants.AnyAsync(item => item.ActivityId == activity.Id && item.IssuedCount > 0, ct))
                return new(Failure: QuestionnaireActivityError.SessionsAlreadyIssued);
            activity.State = QuestionnaireActivityState.Draft; activity.PublishedAt = null;
            foreach (var pause in activity.Pauses.Where(pause => pause.EndedAt is null)) pause.EndedAt = command.Now;
            foreach (var rule in activity.Rules) { db.RemoveRange(rule.Candidates); rule.Candidates.Clear(); }
            operation = QuestionnaireActivityOperationKind.Withdrawn;
        }
        else
        {
            if (activity.State != QuestionnaireActivityState.Published) return new(Failure: QuestionnaireActivityError.InvalidTransition);
            switch (command.Action)
            {
                case QuestionnaireActivityAction.Pause:
                    if (command.Now >= context.Deadline.At || activity.Pauses.Any(pause => pause.EndedAt is null)) return new(Failure: QuestionnaireActivityError.InvalidTransition);
                    var newPause = new QuestionnaireActivityPause { Id = Guid.NewGuid(), ActivityId = activity.Id, StartedAt = command.Now };
                    activity.Pauses.Add(newPause); db.Add(newPause); operation = QuestionnaireActivityOperationKind.Paused;
                    break;
                case QuestionnaireActivityAction.Resume:
                    var pause = activity.Pauses.SingleOrDefault(pause => pause.EndedAt is null);
                    if (pause is null) return new(Failure: QuestionnaireActivityError.InvalidTransition);
                    pause.EndedAt = command.Now; operation = QuestionnaireActivityOperationKind.Resumed;
                    break;
                case QuestionnaireActivityAction.StopIssuance:
                    activity.IssuanceStopped = true; operation = QuestionnaireActivityOperationKind.IssuanceStopped;
                    break;
                case QuestionnaireActivityAction.ExtendDeadline:
                    if (command.Now >= context.Deadline.At || command.Deadline is not { } deadline || deadline <= activity.ClosesAt)
                        return new(Failure: QuestionnaireActivityError.DeadlineExpired);
                    activity.ClosesAt = deadline; operation = QuestionnaireActivityOperationKind.DeadlineExtended;
                    break;
                case QuestionnaireActivityAction.Finish:
                case QuestionnaireActivityAction.Void:
                    if (string.IsNullOrWhiteSpace(command.Reason)) return new(Failure: QuestionnaireActivityError.InvalidConfiguration);
                    activity.State = command.Action == QuestionnaireActivityAction.Finish ? QuestionnaireActivityState.Finished : QuestionnaireActivityState.Voided;
                    activity.FinishedAt = command.Now; activity.IssuanceStopped = true;
                    operation = command.Action == QuestionnaireActivityAction.Finish ? QuestionnaireActivityOperationKind.Finished : QuestionnaireActivityOperationKind.Voided;
                    if (command.Action == QuestionnaireActivityAction.Void)
                    {
                        var sessions = await db.QuestionnaireSessions
                            .Include(session => session.Assignments).ThenInclude(assignment => assignment.Fact).ThenInclude(fact => fact!.CapabilityOutcomes)
                            .Where(session => db.QuestionnaireParticipants.Any(participant => participant.Id == session.ParticipantId && participant.ActivityId == activity.Id))
                            .AsSplitQuery().ToArrayAsync(ct);
                        foreach (var session in sessions) { session.State = QuestionnaireSessionState.Voided; QuestionnaireFactResults.Write(session, command.Now); }
                        var slots = await db.Set<QuestionnaireActiveSession>().Where(slot => db.QuestionnaireParticipants.Any(participant => participant.Id == slot.ParticipantId
                            && participant.ActivityId == activity.Id)).ToArrayAsync(ct);
                        db.RemoveRange(slots);
                    }
                    break;
                default: return new(Failure: QuestionnaireActivityError.InvalidTransition);
            }
        }
        db.Entry(activity).Property(item => item.ConcurrencyStamp).IsModified = true;
        db.Add(Audit(command, operation)); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (signals is not null) await signals.PublishAsync(new(activity.Id, null), ct);
        return new(QuestionnaireActivityMapping.Summary(activity));
    }

    private async Task<QuestionnaireActivityResult?> FreezeCandidatesAsync(QuestionnaireActivity activity, Guid actorId,
        DateTimeOffset now, CancellationToken ct, bool persist = true)
    {
        var isAdmin = await db.Users.AnyAsync(user => user.Id == actorId && user.Role == UserRole.Administrator, ct);
        var bank = db.Set<QuestionnaireCapabilityDefinition>().AsNoTracking()
            .Where(capability => db.Challenges.Any(challenge => challenge.Id == capability.ChallengeId && challenge.DeletedAt == null
                && challenge.CatalogScope == ChallengeCatalogScope.QuestionBank
                && (isAdmin || challenge.OwnerId == actorId || challenge.Visibility == ChallengeVisibility.Shared
                    || challenge.Managers.Any(manager => manager.UserId == actorId))));
        var available = await bank.SelectMany(capability => capability.CurrentRevision.Questions.Select(question => new
        {
            Id = question.VersionId, question.SourceId, capability.ChallengeId, QuestionnaireRevisionId = capability.CurrentRevisionId,
            question.Position, question.Kind, question.Difficulty,
            Classifications = question.Classifications.Select(item => new { item.Kind, item.NormalizedName }).ToArray()
        })).AsSplitQuery().ToArrayAsync(ct);
        var exposed = await db.Set<QuestionnaireQuestionExposure>().Select(exposure => exposure.SourceId)
            .Union(db.Set<QuestionnaireQuestionSource>().Where(source => source.FirstExposedAt != null).Select(source => source.Id)).ToArrayAsync(ct);
        var reserved = activity.Kind == QuestionnaireActivityKind.Examination ? []
            : await QuestionnaireReservations.ReadAsync(db, available.Select(item => item.SourceId).Distinct().ToArray(), activity.Id, now, ct);
        if (activity.SelectionKind == QuestionnaireSelectionKind.Fixed)
        {
            var fixedIds = activity.FixedQuestionnaires.Select(value => value.ChallengeId).ToArray();
            var selected = available.Where(value => fixedIds.Contains(value.ChallengeId)).ToArray();
            if (selected.Select(value => value.ChallengeId).Distinct().Count() != fixedIds.Length) return new(Failure: QuestionnaireActivityError.InsufficientQuestions);
            if (activity.Kind == QuestionnaireActivityKind.Examination && !activity.AllowExposedQuestions && selected.Any(value => exposed.Contains(value.SourceId)))
                return new(Failure: QuestionnaireActivityError.ExposedQuestions);
            if (activity.Kind == QuestionnaireActivityKind.Practice && selected.Any(value => reserved.Contains(value.SourceId)))
                return new(Failure: QuestionnaireActivityError.InsufficientQuestions);
            if (persist && activity.Rules.Count != 0) { db.RemoveRange(activity.Rules); activity.Rules.Clear(); await db.SaveChangesAsync(ct); }
            else activity.Rules = [];
            foreach (var fixedQuestionnaire in activity.FixedQuestionnaires.OrderBy(value => value.Position))
            {
                var contents = selected.Where(value => value.ChallengeId == fixedQuestionnaire.ChallengeId).OrderBy(value => value.Position).ToArray();
                fixedQuestionnaire.RevisionId = contents[0].QuestionnaireRevisionId;
                foreach (var question in contents)
                {
                    var ruleId = Guid.NewGuid();
                    var rule = new QuestionnaireDrawRule { Id = ruleId, ActivityId = activity.Id, Order = activity.Rules.Count, Kind = question.Kind,
                        Count = 1, Points = fixedQuestionnaire.PointsPerQuestion,
                        Candidates = [new() { RuleId = ruleId, RevisionId = question.Id, SourceId = question.SourceId }] };
                    activity.Rules.Add(rule); if (persist) db.Add(rule);
                }
            }
            try
            {
                var total = activity.Rules.Aggregate(0L, (sum, rule) => checked(sum + rule.Points));
                _ = checked(total * (activity.ScoringMode == QuestionnaireScoringMode.Cumulative ? activity.AttemptLimit!.Value : 1));
            }
            catch (OverflowException) { return new(Failure: QuestionnaireActivityError.InvalidConfiguration, Validation: QuestionnaireActivityFailure.PointsOverflow); }
            return null;
        }
        foreach (var rule in activity.Rules)
            rule.Candidates = available.Where(candidate => candidate.Kind == rule.Kind && (rule.Difficulty is null || candidate.Difficulty == rule.Difficulty)
                && rule.Filters.All(filter => candidate.Classifications.Any(item => item.Kind == filter.Kind && item.NormalizedName == filter.NormalizedName))
                && (activity.Kind != QuestionnaireActivityKind.Examination || activity.AllowExposedQuestions || !exposed.Contains(candidate.SourceId))
                && (activity.Kind == QuestionnaireActivityKind.Examination || !reserved.Contains(candidate.SourceId)))
                .Select(candidate => new QuestionnaireDrawCandidate { RuleId = rule.Id, RevisionId = candidate.Id, SourceId = candidate.SourceId }).ToList();
        return null;
    }

    public async Task<QuestionnaireActivityCheck?> CheckAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct, includeCandidates: true);
        if (access is null || !access.CanManage) return null;
        if (access.Activity.State == QuestionnaireActivityState.Draft && await FreezeCandidatesAsync(access.Activity, actorId, now, ct, persist: false) is not null)
        { db.ChangeTracker.Clear(); return new(false, [], []); }
        var attempts = access.Activity.SelectionKind == QuestionnaireSelectionKind.Fixed ? 1 : access.Activity.AttemptLimit ?? 1;
        var result = QuestionnaireSessionAllocator.Allocate(access.Activity.Rules, new HashSet<Guid>(), attempts, ct);
        var check = new QuestionnaireActivityCheck(result.Succeeded, access.Activity.Rules.OrderBy(rule => rule.Order)
            .Select(rule => new QuestionnaireRuleCapacity(rule.Order, checked(rule.Count * attempts),
                rule.Candidates.Select(candidate => candidate.SourceId).Distinct().Count())).ToArray(), result.Shortages);
        db.ChangeTracker.Clear(); return check;
    }

    public async Task<QuestionnaireActivityPage> ListAsync(Guid actorId, bool staff, CancellationToken ct,
        int offset = 0, int limit = 25, string? keyword = null)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active, ct);
        if (user is null) return new([], 0);
        var query = db.QuestionnaireActivities.AsNoTracking().Include(activity => activity.Rules).AsQueryable();
        if (staff) query = query.Where(activity => user.Role == UserRole.Administrator || activity.OwnerId == actorId);
        if (!staff) query = query.Where(activity => activity.State != QuestionnaireActivityState.Draft && activity.State != QuestionnaireActivityState.Voided);
        if (!string.IsNullOrWhiteSpace(keyword)) { var search = keyword.Trim().ToUpperInvariant(); query = query.Where(activity => activity.Name.ToUpper().Contains(search)); }
        var total = await query.CountAsync(ct);
        var activities = await query.OrderByDescending(activity => activity.CreatedAt).ThenBy(activity => activity.Id)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 200)).ToArrayAsync(ct);
        return new(activities.Select(QuestionnaireActivityMapping.Summary).ToArray(), total);
    }

    public async Task<QuestionnaireActivityManagement?> ReadManagementAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await QuestionnaireActivityAccess.ReadAsync(db, activityId, actorId, now, ct);
        if (access is null || !access.CanObserve) return null;
        var issued = await db.QuestionnaireParticipants.Where(participant => participant.ActivityId == activityId).SumAsync(participant => participant.IssuedCount, ct);
        var operations = new List<QuestionnaireActivityAction>();
        if (access.CanManage)
        {
            if (access.Activity.State == QuestionnaireActivityState.Draft && now < access.Activity.ClosesAt) operations.Add(QuestionnaireActivityAction.Publish);
            if (access.Activity.State == QuestionnaireActivityState.Published)
            {
                if (issued == 0) operations.Add(QuestionnaireActivityAction.Withdraw);
                if (now < access.Deadline.At)
                {
                    operations.Add(access.Activity.Pauses.Any(pause => pause.EndedAt is null) ? QuestionnaireActivityAction.Resume : QuestionnaireActivityAction.Pause);
                    operations.Add(QuestionnaireActivityAction.ExtendDeadline);
                }
                if (!access.Activity.IssuanceStopped) operations.Add(QuestionnaireActivityAction.StopIssuance);
                operations.Add(QuestionnaireActivityAction.Finish); operations.Add(QuestionnaireActivityAction.Void);
            }
        }
        return new(QuestionnaireActivityMapping.Summary(access.Activity), access.Activity.Rules.OrderBy(rule => rule.Order)
            .Select(rule => new QuestionnaireRuleInput(rule.Kind, rule.Difficulty, rule.Count, rule.Points,
                rule.Filters.Select(filter => new QuestionnaireClassificationFilter(filter.Kind, filter.NormalizedName)).ToArray())).ToArray(),
            access.Activity.AllowExposedQuestions, access.CanManage, access.CanJudge, access.Deadline)
        {
            SelectionKind = access.Activity.SelectionKind,
            FixedQuestionnaires = access.Activity.FixedQuestionnaires.OrderBy(value => value.Position).Select(value => new QuestionnaireFixedSelectionInput(value.ChallengeId, value.PointsPerQuestion)).ToArray(),
            Corrections = await db.Set<QuestionnaireGradingOverride>().AsNoTracking().Where(rule => rule.ActivityId == activityId)
                .Select(rule => new QuestionnaireCorrectionProgress(rule.RevisionId, rule.TargetRevision, rule.AppliedRevision == rule.TargetRevision)).ToArrayAsync(ct),
            IssuedCount = issued, AllowedOperations = operations
        };
    }

    private static QuestionnaireActivityOperation Audit(QuestionnaireActivityTransition command, QuestionnaireActivityOperationKind kind) => new()
    { Id = Guid.NewGuid(), ActivityId = command.ActivityId, ActorId = command.ActorId, OccurredAt = command.Now, Kind = kind, Reason = command.Reason };

    private async Task<QuestionnaireActivityResult> MutateAsync(Func<Task<QuestionnaireActivityResult>> operation)
    {
        try { return await operation(); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || exception.GetBaseException() is DbException { SqlState: "40001" or "40P01" or "23505" })
        { db.ChangeTracker.Clear(); return new(Failure: QuestionnaireActivityError.Conflict); }
    }
}

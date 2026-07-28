using NoCTF.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Submissions.Management;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Submissions.Management;

public sealed class SubmissionManagementStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox) : ISubmissionManagementStore
{
    public async Task<IReadOnlyList<SubmissionListItem>> ListAdminAsync(
        SubmissionListFilter filter,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var query = Query().Where(item => item.Submission.CompetitionId == filter.CompetitionId);
        if (filter.CompetitionChallengeId is Guid challengeId)
            query = query.Where(item => item.Submission.CompetitionChallengeId == challengeId);
        if (filter.TeamId is Guid teamId)
            query = query.Where(item => item.Submission.TeamId == teamId);
        if (filter.UserId is Guid userId)
            query = query.Where(item => item.Submission.SubmittedByUserId == userId);
        if (filter.Kind is SubmissionKind kind)
            query = query.Where(item => item.Submission.Kind == kind);
        if (filter.EvaluationState is SubmissionEvaluationState state)
            query = query.Where(item => item.Submission.EvaluationState == state);
        if (filter.Result is ScoringResult result)
            query = query.Where(item => item.Event != null && item.Event.Result == result);
        if (filter.FailureCode is ScoringFailureCode failure)
            query = query.Where(item =>
                item.Submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                    ? item.Submission.EvaluationFailureCode == failure
                    : item.Event != null && item.Event.FailureCode == failure);
        if (filter.ReceivedFrom is DateTimeOffset from)
            query = query.Where(item => item.Submission.ReceivedAt >= from);
        if (filter.ReceivedTo is DateTimeOffset to)
            query = query.Where(item => item.Submission.ReceivedAt < to);
        if (!string.IsNullOrEmpty(filter.SubmittedFlag))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filter.SubmittedFlag));
            query = query.Where(item => item.Submission.SubmittedFlagSha256 == hash);
        }
        if (filter.HasCurrentScoringEvent is bool hasEvent)
            query = query.Where(item => (item.Submission.CurrentScoringEventId != null) == hasEvent);
        return await Page(query, beforeReceivedAt, beforeId, limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SubmissionListItem>?> ListPlayerAsync(
        Guid competitionId,
        Guid userId,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.MemberIds.Contains(userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(ct);
        if (teamId is null)
            return null;
        return await Page(
                Query().Where(item =>
                    item.Submission.CompetitionId == competitionId &&
                    item.Submission.TeamId == teamId.Value),
                beforeReceivedAt,
                beforeId,
                limit)
            .ToListAsync(ct);
    }

    public async Task QueueDrainAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? submissionId,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await outbox.PublishAsync(new DrainSubmissions(
            competitionId, competitionChallengeId, cutoff, rejudge, submissionId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private IQueryable<SubmissionWithEvent> Query() =>
        db.Submissions.AsNoTracking()
            .GroupJoin(
                db.ScoringEvents.AsNoTracking(),
                submission => submission.CurrentScoringEventId,
                scoringEvent => (Guid?)scoringEvent.Id,
                (submission, events) => new { Submission = submission, Events = events })
            .SelectMany(
                item => item.Events.DefaultIfEmpty(),
                (item, scoringEvent) => new SubmissionWithEvent(item.Submission, scoringEvent));

    private static IQueryable<SubmissionListItem> Page(
        IQueryable<SubmissionWithEvent> query,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit)
    {
        if (beforeReceivedAt is DateTimeOffset receivedAt && beforeId is Guid id)
            query = query.Where(item =>
                item.Submission.ReceivedAt < receivedAt ||
                item.Submission.ReceivedAt == receivedAt && item.Submission.Id.CompareTo(id) < 0);
        return query
            .OrderByDescending(item => item.Submission.ReceivedAt)
            .ThenByDescending(item => item.Submission.Id)
            .Take(limit)
            .Select(item => new SubmissionListItem(
                item.Submission.Id,
                item.Submission.CompetitionId,
                item.Submission.CompetitionChallengeId,
                item.Submission.TeamId,
                item.Submission.SubmittedByUserId,
                item.Submission.Kind,
                item.Submission.EvaluationState,
                item.Event == null ? null : item.Event.Result,
                item.Submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                    ? item.Submission.EvaluationFailureCode
                    : item.Event == null ? null : item.Event.FailureCode,
                item.Submission.ReceivedAt,
                item.Submission.ProcessingVersion));
    }

    private sealed record SubmissionWithEvent(Submission Submission, ScoringEvent? Event);
}

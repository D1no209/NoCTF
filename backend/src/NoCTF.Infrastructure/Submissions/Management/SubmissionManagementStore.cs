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
        var query = db.Submissions.AsNoTracking()
            .Where(submission => submission.CompetitionId == filter.CompetitionId);
        if (filter.CompetitionChallengeId is Guid challengeId)
            query = query.Where(submission =>
                submission.CompetitionChallengeId == challengeId);
        if (filter.TeamId is Guid teamId)
            query = query.Where(submission => submission.TeamId == teamId);
        if (filter.UserId is Guid userId)
            query = query.Where(submission => submission.SubmittedByUserId == userId);
        if (filter.Kind is SubmissionKind kind)
            query = query.Where(submission => submission.Kind == kind);
        if (filter.EvaluationState is SubmissionEvaluationState state)
            query = query.Where(submission => submission.EvaluationState == state);
        if (filter.Result is ScoringResult result)
            query = query.Where(submission => db.ScoringEvents.Any(scoringEvent =>
                scoringEvent.Id == submission.CurrentScoringEventId
                && scoringEvent.Result == result));
        if (filter.FailureCode is ScoringFailureCode failure)
            query = query.Where(submission =>
                submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                    ? submission.EvaluationFailureCode == failure
                    : db.ScoringEvents.Any(scoringEvent =>
                        scoringEvent.Id == submission.CurrentScoringEventId
                        && scoringEvent.FailureCode == failure));
        if (filter.ReceivedFrom is DateTimeOffset from)
            query = query.Where(submission => submission.ReceivedAt >= from);
        if (filter.ReceivedTo is DateTimeOffset to)
            query = query.Where(submission => submission.ReceivedAt < to);
        if (!string.IsNullOrEmpty(filter.SubmittedFlag))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(filter.SubmittedFlag));
            query = query.Where(submission => submission.SubmittedFlagSha256 == hash);
        }
        if (filter.HasCurrentScoringEvent is bool hasEvent)
            query = query.Where(submission =>
                (submission.CurrentScoringEventId != null) == hasEvent);
        return await Project(Page(query, beforeReceivedAt, beforeId, limit))
            .ToListAsync(ct);
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
        var submissions = db.Submissions.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId
                && submission.TeamId == teamId.Value);
        return await Project(Page(
                submissions,
                beforeReceivedAt,
                beforeId,
                limit))
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

    private static IQueryable<Submission> Page(
        IQueryable<Submission> query,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit)
    {
        if (beforeReceivedAt is DateTimeOffset receivedAt && beforeId is Guid id)
            query = query.Where(submission =>
                submission.ReceivedAt < receivedAt
                || submission.ReceivedAt == receivedAt
                && submission.Id.CompareTo(id) < 0);
        return query
            .OrderByDescending(submission => submission.ReceivedAt)
            .ThenByDescending(submission => submission.Id)
            .Take(limit);
    }

    private IQueryable<SubmissionListItem> Project(IQueryable<Submission> submissions) =>
        submissions.Select(submission => new SubmissionListItem(
            submission.Id,
            submission.CompetitionId,
            submission.CompetitionChallengeId,
            submission.TeamId,
            submission.SubmittedByUserId,
            submission.Kind,
            submission.EvaluationState,
            db.ScoringEvents
                .Where(scoringEvent =>
                    scoringEvent.Id == submission.CurrentScoringEventId)
                .Select(scoringEvent => (ScoringResult?)scoringEvent.Result)
                .SingleOrDefault(),
            submission.EvaluationState == SubmissionEvaluationState.PlatformFailed
                ? submission.EvaluationFailureCode
                : db.ScoringEvents
                    .Where(scoringEvent =>
                        scoringEvent.Id == submission.CurrentScoringEventId)
                    .Select(scoringEvent => scoringEvent.FailureCode)
                    .SingleOrDefault(),
            submission.ReceivedAt,
            submission.ProcessingVersion));
}

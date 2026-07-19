using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Evaluates a submission once and binds its current score-free event.</summary>
public sealed class EfSubmissionProcessor(
    NoCtfDbContext db,
    IBackgroundWorkScheduler scheduler,
    ILogger<EfSubmissionProcessor> logger) : ISubmissionProcessor
{
    public Task ProcessFlagAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken);

    public Task ProcessFixAsync(Guid competitionId, Guid submissionId, CancellationToken cancellationToken) =>
        ProcessAsync(competitionId, submissionId, cancellationToken);

    private async Task ProcessAsync(Guid competitionId, Guid submissionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var submission = await db.Submissions.Include(x => x.ScoringEvent)
            .SingleOrDefaultAsync(x => x.Id == submissionId && (competitionId == Guid.Empty || x.CompetitionId == competitionId), ct);
        if (submission is null || submission.ScoringEvent is not null) return;
        if (submission.TeamId is not Guid teamId || submission.ChallengeId is not Guid challengeId) return;

        var result = await EvaluateAsync(submission, ct);
        var now = DateTimeOffset.UtcNow;
        var scoringEvent = new ScoringEvent
        {
            Id = Guid.CreateVersion7(now), CompetitionId = submission.CompetitionId, TeamId = teamId,
            ChallengeId = challengeId, SubmissionId = submission.Id, Kind = ScoringEventKind.SubmissionEvaluation,
            Result = result.Result, FailureCode = result.FailureCode, OccurredAt = submission.ReceivedAt,
            ProcessedAt = now, ProcessedWorkerId = Environment.MachineName, EvaluatorVersion = "ef-v1", CreatedAt = now
        };
        db.ScoringEvents.Add(scoringEvent);
        submission.ScoringEventId = scoringEvent.Id;
        submission.ProcessingVersion++;
        submission.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, ct);
        logger.LogInformation("Processed submission {SubmissionId} as {Result}", submissionId, result.Result);
    }

    private async Task<(ScoringResult Result, ScoringFailureCode? FailureCode)> EvaluateAsync(Submission submission, CancellationToken ct)
    {
        if (submission.Kind == SubmissionKind.Fix)
        {
            var fix = await db.FixSubmissionRecords.SingleOrDefaultAsync(x => x.SubmissionId == submission.Id, ct);
            return fix?.VerificationStatus switch
            {
                NoCTF.Domain.Submissions.FixVerificationStatus.Valid => (ScoringResult.Correct, null),
                NoCTF.Domain.Submissions.FixVerificationStatus.PlatformFailed => (ScoringResult.PlatformFailed, ScoringFailureCode.CheckerPlatformError),
                _ => (ScoringResult.Rejected, ScoringFailureCode.FixArchiveMissing)
            };
        }

        var hasPriorCorrect = await db.ScoringEvents.AnyAsync(x => x.CompetitionId == submission.CompetitionId
            && x.TeamId == submission.TeamId && x.ChallengeId == submission.ChallengeId
            && x.SubmissionId != submission.Id && x.Result == ScoringResult.Correct, ct);
        if (hasPriorCorrect) return (ScoringResult.Duplicate, null);
        var valid = await db.ChallengeFlags.AnyAsync(x => x.CompetitionId == submission.CompetitionId
            && x.ChallengeId == submission.ChallengeId && (x.TeamId == null || x.TeamId == submission.TeamId)
            && (x.ValidStart == null || x.ValidStart <= submission.ReceivedAt)
            && (x.ValidEnd == null || x.ValidEnd >= submission.ReceivedAt)
            && x.Flag == submission.Flag, ct);
        return valid ? (ScoringResult.Correct, null) : (ScoringResult.Wrong, null);
    }
}

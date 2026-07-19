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
    ISubmissionEvaluator evaluator,
    ILogger<EfSubmissionProcessor> logger) : ISubmissionProcessor
{
    public async Task ProcessAsync(Guid submissionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var submission = await db.Submissions.Include(x => x.ScoringEvent)
            .SingleOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null || submission.ScoringEvent is not null) return;
        if (submission.TeamId is not Guid teamId || submission.ChallengeId is not Guid challengeId) return;

        var result = await EvaluateAsync(submission, ct);
        var now = DateTimeOffset.UtcNow;
        var scoringEvent = new ScoringEvent
        {
            Id = Guid.CreateVersion7(now), CompetitionId = submission.CompetitionId, TeamId = teamId,
            ChallengeId = challengeId, SubmissionId = submission.Id, Kind = ScoringEventKind.SubmissionEvaluation,
            Result = result.Result, FailureCode = result.FailureCode, OccurredAt = result.OccurredAt,
            ProcessedAt = now, ProcessedWorkerId = Environment.MachineName, EvaluatorVersion = result.EvaluatorVersion, CreatedAt = now
        };
        db.ScoringEvents.Add(scoringEvent);
        submission.ScoringEventId = scoringEvent.Id;
        submission.ProcessingVersion++;
        submission.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await scheduler.EnqueueLeaderboardRefreshAsync(submission.CompetitionId, ct);
        logger.LogInformation("Processed submission {SubmissionId} as {Result}", submissionId, result.Result);
    }

    private async Task<ScoringEventDecision> EvaluateAsync(Submission submission, CancellationToken ct)
    {
        var prior = await db.ScoringEvents.Where(x => x.CompetitionId == submission.CompetitionId && x.SubmissionId != submission.Id).ToListAsync(ct);
        var flags = await db.ChallengeFlags.Where(x => x.CompetitionId == submission.CompetitionId
            && x.ChallengeId == submission.ChallengeId && (x.TeamId == null || x.TeamId == submission.TeamId)
            && (x.ValidStart == null || x.ValidStart <= submission.ReceivedAt)
            && (x.ValidEnd == null || x.ValidEnd >= submission.ReceivedAt)).ToListAsync(ct);
        var fix = await db.FixSubmissionRecords.SingleOrDefaultAsync(x => x.SubmissionId == submission.Id, ct);
        return evaluator.Evaluate(new(submission, prior, flags, fix, string.Empty, string.Empty));
    }
}

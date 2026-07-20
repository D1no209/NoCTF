using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

/// <summary>Evaluates a submission once and binds its current score-free event.</summary>
public sealed class EfSubmissionProcessor(
    NoCtfDbContext db,
    IBackgroundWorkScheduler scheduler,
    ISubmissionEvaluatorCatalog evaluatorCatalog,
    ISubmissionAdmissionModePolicy admissionModePolicy,
    VerifyFixSubmission verifyFixSubmission,
    ILogger<EfSubmissionProcessor> logger) : ISubmissionProcessor
{
    public async Task ProcessAsync(Guid submissionId, CancellationToken ct)
    {
        var kind = await db.Submissions.AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new { submission.Kind, submission.ScoringEventId })
            .SingleOrDefaultAsync(ct);
        if (kind is null)
            return;
        if (kind.ScoringEventId is not null)
            return;
        if (kind.Kind == SubmissionKind.Fix)
        {
            var verification = await verifyFixSubmission.ExecuteAsync(submissionId, DateTimeOffset.UtcNow, ct);
            if (!verification.Succeeded && verification.Error == FixVerificationError.Concurrency)
                return;
        }

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
        var configuration = await (
            from competition in db.Competitions.AsNoTracking()
            join competitionConfiguration in db.CompetitionConfigurations.AsNoTracking()
                on competition.Id equals competitionConfiguration.CompetitionId
            join challengeConfiguration in db.ChallengeConfigurations.AsNoTracking()
                on submission.ChallengeId equals challengeConfiguration.ChallengeId
            where competition.Id == submission.CompetitionId
            select new
            {
                competition.Mode,
                competition.StartTime,
                CompetitionJson = competitionConfiguration.Json,
                ChallengeJson = challengeConfiguration.Json
            }).SingleAsync(ct);
        var rules = admissionModePolicy.GetRules(
            configuration.Mode,
            configuration.CompetitionJson,
            configuration.ChallengeJson);
        var maxAttempts = submission.Kind == SubmissionKind.Flag
            ? rules.MaxFlagAttempts
            : rules.MaxFixAttempts;
        if (maxAttempts is > 0)
        {
            var acceptedIds = await db.Submissions.AsNoTracking()
                .Where(candidate => candidate.CompetitionId == submission.CompetitionId
                                    && candidate.TeamId == submission.TeamId
                                    && candidate.ChallengeId == submission.ChallengeId
                                    && candidate.Kind == submission.Kind)
                .OrderBy(candidate => candidate.ReceivedAt)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => candidate.Id)
                .ToListAsync(ct);
            var ordinal = acceptedIds.IndexOf(submission.Id) + 1;
            if (ordinal > maxAttempts)
                return new(
                    ScoringEventKind.SubmissionEvaluation,
                    ScoringResult.AttemptsExhausted,
                    null,
                    submission.ReceivedAt,
                    "admission-v1");
        }

        var prior = await db.ScoringEvents.Where(x => x.CompetitionId == submission.CompetitionId && x.SubmissionId != submission.Id).ToListAsync(ct);
        var priorSubmissions = await db.Submissions.AsNoTracking()
            .Where(x => x.CompetitionId == submission.CompetitionId && x.Id != submission.Id)
            .ToListAsync(ct);
        var flagTeamId = configuration.Mode == GameMode.Awd
            ? submission.SubjectTeamId ?? submission.VictimTeamId
            : submission.TeamId;
        var flags = await db.ChallengeFlags.Where(x => x.CompetitionId == submission.CompetitionId
            && x.ChallengeId == submission.ChallengeId
            && (x.TeamId == null || x.TeamId == flagTeamId)).ToListAsync(ct);
        var fix = await db.FixSubmissionRecords.SingleOrDefaultAsync(x => x.SubmissionId == submission.Id, ct);
        return evaluatorCatalog.Get(configuration.Mode).Evaluate(new(
            submission,
            prior,
            flags,
            fix,
            configuration.CompetitionJson,
            configuration.ChallengeJson,
            priorSubmissions,
            configuration.StartTime));
    }
}

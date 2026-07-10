using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NoCTF.Plugins.Penetration;

public class PenetrationSubmissionHandler(
    ApplicationDbContext db,
    PenetrationFlagService flagService,
    IScoreSignalEmitter scoreSignalEmitter,
    ISubmissionEventHandler submissionEventHandler,
    ILogger<PenetrationSubmissionHandler>? logger = null)
    : IChallengeSubmissionHandler
{
    public string TypeId => PenetrationConstants.TypeId;

    public async Task<ChallengeSubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        Challenge challenge,
        CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, ct);
        if (competition is null) return new ChallengeSubmissionResult(SubmissionResult.WrongFlag);

        var now = DateTime.UtcNow;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionNotStarted);
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionEnded);
        if (competition.Status == CompetitionStatus.Paused)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionPaused);

        var instance = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.CompetitionId == context.CompetitionId &&
                i.TeamId == context.TeamId &&
                i.ChallengeId == context.ChallengeId,
                ct);

        if (instance is null || instance.Status != PenetrationInstanceStatus.Running)
            return new ChallengeSubmissionResult(SubmissionResult.InstanceRequired);
        if (instance.ExpiresAt is not null && instance.ExpiresAt <= now)
            return new ChallengeSubmissionResult(SubmissionResult.InstanceExpired);

        var match = await flagService.MatchAsync(challenge, instance, context.FlagContent, ct);
        if (!match.IsCorrect)
        {
            if (await IsRateLimitedAsync(context, now, ct))
            {
                AddCompetitionLog(
                    context,
                    "penetration.flag.rate_limited",
                    "Team submitted penetration flags too frequently.",
                    "warning",
                    new { submitted = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent) });
                await db.SaveChangesAsync(ct);
                return new ChallengeSubmissionResult(SubmissionResult.FlagRateLimited);
            }

            await RecordRejectedSubmissionAsync(context, challenge, match, now, ct);
            return new ChallengeSubmissionResult(SubmissionResult.WrongFlag);
        }

        var flag = match.Flag!;
        var alreadySolved = await HasCorrectStageSolveAsync(context, flag.Id, ct);
        if (alreadySolved)
            return new ChallengeSubmissionResult(SubmissionResult.AlreadySolved, BuildResultData(flag, true));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            PenetrationFlagId = flag.Id,
            UserId = context.UserId,
            FlagContent = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
            IsCorrect = true,
            SubmittedAt = now,
            IpAddress = context.IpAddress
        };

        db.Submissions.Add(submission);
        AddCompetitionLog(
            context,
            "penetration.flag.accepted",
            $"Team solved penetration stage {flag.Stage} for challenge {challenge.Title}.",
            "info",
            new { flagId = flag.Id, flag.Stage, flagName = flag.Name, submissionId = submission.Id });
        await flagService.MarkSolvedAsync(context.CompetitionId, context.TeamId, context.ChallengeId, flag.Id, now, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            if (await HasCorrectStageSolveAsync(context, flag.Id, ct))
                return new ChallengeSubmissionResult(SubmissionResult.AlreadySolved, BuildResultData(flag, true));
            throw;
        }

        await scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
            CompetitionId: context.CompetitionId,
            TeamId: context.TeamId,
            SignalType: ScoreSignalTypes.PenetrationFlagAccepted,
            IdempotencyKey: $"penetration:{context.TeamId:N}:{context.ChallengeId:N}:{flag.Id:N}",
            SubjectType: "penetration-flag",
            SubjectId: flag.Id,
            ActorUserId: context.UserId,
            PayloadJson: ScoringJson.Serialize(new
            {
                submissionId = submission.Id,
                challengeId = context.ChallengeId,
                flagId = flag.Id,
                flag.Stage,
                flagName = flag.Name,
                score = flag.Score
            }),
            OccurredAt: now), ct);

        await transaction.CommitAsync(ct);

        var team = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == context.TeamId, ct);
        var awarded = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == context.CompetitionId &&
                e.TeamId == context.TeamId &&
                e.ChallengeId == context.ChallengeId &&
                (e.ScoringKey == ScoringKeys.PenetrationStage ||
                 e.ScoringKey == ScoringKeys.PenetrationBloodBonus))
            .SumAsync(e => e.PointsDelta, ct);

        try
        {
            await submissionEventHandler.HandleAsync(new SubmissionSolvedEvent(
                CompetitionId: context.CompetitionId,
                ChallengeId: context.ChallengeId,
                ChallengeName: challenge.Title,
                TeamId: context.TeamId,
                TeamName: team?.Name ?? context.TeamId.ToString(),
                IsFirstBlood: flag.SolvedCount == 0,
                PointsAwarded: awarded), ct);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<PenetrationSubmissionHandler>.Instance).LogError(
                ex,
                "Post-solve notification failed for penetration submission {SubmissionId}; the accepted stage remains committed.",
                submission.Id);
        }

        return new ChallengeSubmissionResult(SubmissionResult.Accepted, BuildResultData(flag, false));
    }

    private async Task RecordRejectedSubmissionAsync(
        SubmissionContext context,
        Challenge challenge,
        PenetrationFlagMatch match,
        DateTime now,
        CancellationToken ct)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            UserId = context.UserId,
            FlagContent = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
            IsCorrect = false,
            SubmittedAt = now,
            IpAddress = context.IpAddress
        });

        AddCompetitionLog(
            context,
            match.IsCrossTeamDynamicFlag ? "penetration.flag.suspected_cross_team" : "penetration.flag.rejected",
            match.IsCrossTeamDynamicFlag
                ? $"Team submitted another team's penetration dynamic flag for challenge {challenge.Title}."
                : $"Team submitted a wrong penetration flag for challenge {challenge.Title}.",
            match.IsCrossTeamDynamicFlag ? "error" : "warning",
            new
            {
                submitted = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
                victimTeamId = match.VictimTeamId,
                flagId = match.Flag?.Id
            });

        if (match.IsCrossTeamDynamicFlag && match.VictimTeamId.HasValue)
        {
            db.CheatIncidents.Add(new CheatIncident
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                SuspectTeamId = context.TeamId,
                VictimTeamId = match.VictimTeamId.Value,
                ChallengeId = context.ChallengeId,
                UserId = context.UserId,
                SubmittedFlag = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
                Reason = "submitted_other_team_penetration_dynamic_flag",
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private Task<bool> HasCorrectStageSolveAsync(SubmissionContext context, Guid flagId, CancellationToken ct)
        => db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.PenetrationFlagId == flagId &&
                s.IsCorrect,
                ct);

    private async Task<bool> IsRateLimitedAsync(SubmissionContext context, DateTime now, CancellationToken ct)
    {
        var oneMinuteAgo = now.AddMinutes(-1);
        var stats = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                !s.IsCorrect &&
                s.SubmittedAt >= oneMinuteAgo)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Latest = group.Max(s => s.SubmittedAt)
            })
            .FirstOrDefaultAsync(ct);

        return stats is not null &&
               (stats.Latest >= now.AddSeconds(-5) || stats.Count >= 10);
    }

    private void AddCompetitionLog(
        SubmissionContext context,
        string eventType,
        string message,
        string level,
        object? metadata)
    {
        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            Level = level,
            EventType = eventType,
            Message = message,
            TeamId = context.TeamId,
            UserId = context.UserId,
            ChallengeId = context.ChallengeId,
            MetadataJson = metadata is null ? "{}" : ScoringJson.Serialize(metadata),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static object BuildResultData(PenetrationFlag flag, bool alreadySolved)
        => new
        {
            flagId = flag.Id,
            flag.Stage,
            flag.Name,
            flag.Score,
            alreadySolved
        };
}

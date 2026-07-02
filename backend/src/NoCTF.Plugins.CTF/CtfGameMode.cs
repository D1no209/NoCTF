using Microsoft.EntityFrameworkCore;
using System.Globalization;
using NoCTF.Application;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// CTF game mode: validates flags, prevents duplicate solves, records submissions,
/// emits neutral scoring signals, and fires SignalR notifications via ISubmissionEventHandler.
/// </summary>
public class CtfGameMode : IGameMode
{
    private readonly ApplicationDbContext _db;
    private readonly ISubmissionEventHandler _submissionEventHandler;
    private readonly IScoreSignalEmitter _scoreSignalEmitter;

    public GameModeType Type => GameModeType.Ctf;

    public CtfGameMode(
        ApplicationDbContext db,
        ISubmissionEventHandler submissionEventHandler,
        IScoreSignalEmitter scoreSignalEmitter)
    {
        _db = db;
        _submissionEventHandler = submissionEventHandler;
        _scoreSignalEmitter = scoreSignalEmitter;
    }

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        // Load challenge (tenant filter applied by EF global query filter)
        var challenge = await _db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.ChallengeId, cancellationToken);

        if (challenge is null)
            return SubmissionResult.WrongFlag;

        // Validate flag using timing-safe comparison. New CTF challenges store only
        // the flag content; competition bindings provide the flag prefix.
        var flagSecret = challenge.FlagSecret ?? string.Empty;
        var isDynamicUuid = string.Equals(flagSecret.Trim(), "[UUID]", StringComparison.OrdinalIgnoreCase);
        var expectedFlag = await ResolveExpectedFlagAsync(challenge, context.TeamId, isDynamicUuid, cancellationToken);
        var isCorrect = FlagValidator.IsMatch(context.FlagContent, expectedFlag);

        if (!isCorrect && !isDynamicUuid && LooksLikeLegacyFullFlag(flagSecret))
        {
            isCorrect = FlagValidator.IsMatch(context.FlagContent, flagSecret);
        }

        CtfDynamicFlag? stolenFlag = null;
        if (!isCorrect && isDynamicUuid)
        {
            stolenFlag = await FindStolenDynamicFlagAsync(challenge, context, cancellationToken);
        }

        // Return duplicate solves normally, but still record suspicious cross-team
        // dynamic flag submissions even after the team has already solved the task.
        var alreadySolved = await _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

        if (alreadySolved && stolenFlag is null)
            return SubmissionResult.AlreadySolved;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Record submission regardless of correctness
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            UserId = context.UserId,
            FlagContent = context.FlagContent,
            IsCorrect = isCorrect,
            SubmittedAt = DateTime.UtcNow,
            IpAddress = context.IpAddress
        };

        _db.Submissions.Add(submission);
        _db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            Level = isCorrect ? "info" : stolenFlag is null ? "warning" : "error",
            EventType = isCorrect ? "flag.accepted" : stolenFlag is null ? "flag.rejected" : "flag.suspected_cheat",
            Message = isCorrect
                ? $"Team submitted a correct flag for challenge {challenge.Title}."
                : stolenFlag is null
                    ? $"Team submitted a wrong flag for challenge {challenge.Title}."
                    : $"Team submitted another team's dynamic flag for challenge {challenge.Title}.",
            TeamId = context.TeamId,
            UserId = context.UserId,
            ChallengeId = context.ChallengeId,
            MetadataJson = stolenFlag is null ? "{}" : ScoringJson.Serialize(new { victimTeamId = stolenFlag.TeamId }),
            CreatedAt = submission.SubmittedAt,
        });

        if (stolenFlag is not null)
        {
            _db.CheatIncidents.Add(new CheatIncident
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                SuspectTeamId = context.TeamId,
                VictimTeamId = stolenFlag.TeamId,
                ChallengeId = context.ChallengeId,
                UserId = context.UserId,
                SubmittedFlag = context.FlagContent,
                Reason = "submitted_other_team_dynamic_flag",
                CreatedAt = submission.SubmittedAt,
            });
        }

        if (!isCorrect)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SubmissionResult.WrongFlag;
        }

        // Count existing correct solves for first blood notification only.
        var solveCount = await _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

        // Check if this is first blood (no prior correct solves)
        var isFirstBlood = solveCount == 0;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await HasCorrectSolveAsync(context, cancellationToken))
                throw;

            await transaction.RollbackAsync(cancellationToken);
            return SubmissionResult.AlreadySolved;
        }

        await _scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
            CompetitionId: context.CompetitionId,
            TeamId: context.TeamId,
            SignalType: ScoreSignalTypes.SolveAccepted,
            IdempotencyKey: $"ctf:{context.TeamId:N}:{context.ChallengeId:N}:solve",
            SubjectType: "challenge",
            SubjectId: context.ChallengeId,
            ActorUserId: context.UserId,
            PayloadJson: ScoringJson.Serialize(new { submissionId = submission.Id }),
            OccurredAt: submission.SubmittedAt), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Load team name for notification
        var team = await _db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == context.TeamId, cancellationToken);

        var teamName = team?.Name ?? context.TeamId.ToString();

        var pointsAwarded = await _db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == context.CompetitionId &&
                e.TeamId == context.TeamId &&
                e.ChallengeId == context.ChallengeId &&
                e.ScoringKey == ScoringKeys.DecaySolve)
            .SumAsync(e => e.PointsDelta, cancellationToken);

        // Fire post-solve event: updates leaderboard cache + SignalR notifications
        await _submissionEventHandler.HandleAsync(new SubmissionSolvedEvent(
            CompetitionId: context.CompetitionId,
            ChallengeId: context.ChallengeId,
            ChallengeName: challenge.Title,
            TeamId: context.TeamId,
            TeamName: teamName,
            IsFirstBlood: isFirstBlood,
            PointsAwarded: pointsAwarded), cancellationToken);

        return SubmissionResult.Accepted;
    }

    private Task<bool> HasCorrectSolveAsync(SubmissionContext context, CancellationToken cancellationToken)
        => _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

    private async Task<string> ResolveExpectedFlagAsync(
        Challenge challenge,
        Guid teamId,
        bool isDynamicUuid,
        CancellationToken cancellationToken)
    {
        if (!isDynamicUuid)
            return FormatFlag(challenge, challenge.FlagSecret ?? string.Empty);

        var flag = await _db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                f.CompetitionId == challenge.CompetitionId &&
                f.TeamId == teamId &&
                f.ChallengeId == challenge.Id,
                cancellationToken);

        return flag is null ? string.Empty : FormatFlag(challenge, flag.FlagUuid);
    }

    private async Task<CtfDynamicFlag?> FindStolenDynamicFlagAsync(
        Challenge challenge,
        SubmissionContext context,
        CancellationToken cancellationToken)
    {
        var flags = await _db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == context.CompetitionId &&
                f.ChallengeId == context.ChallengeId &&
                f.TeamId != context.TeamId)
            .ToListAsync(cancellationToken);

        return flags.FirstOrDefault(f =>
            FlagValidator.IsMatch(context.FlagContent, FormatFlag(challenge, f.FlagUuid)) ||
            FlagValidator.IsMatch(context.FlagContent, f.FlagUuid));
    }

    private static string FormatFlag(Challenge challenge, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (prefix.Contains("{0}", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, prefix, content);

        if (prefix.Contains("{}", StringComparison.Ordinal))
            return prefix.Replace("{}", $"{{{content}}}", StringComparison.Ordinal);

        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        return $"{prefix}{{{content}}}";
    }

    private static bool LooksLikeLegacyFullFlag(string value)
        => value.Contains('{', StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal);

}

using Microsoft.EntityFrameworkCore;
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

        // Check for duplicate solve by this team
        var alreadySolved = await _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

        if (alreadySolved)
            return SubmissionResult.AlreadySolved;

        // Validate flag using timing-safe comparison
        var flagSecret = challenge.FlagSecret ?? string.Empty;
        var isCorrect = FlagValidator.IsMatch(context.FlagContent, flagSecret);

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

}

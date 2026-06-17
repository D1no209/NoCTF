using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Events;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// CTF game mode: validates flags, prevents duplicate solves, writes Submission + ScoreEvent,
/// recalculates dynamic scoring, and fires SignalR notifications via ISubmissionEventHandler.
/// </summary>
public class CtfGameMode : IGameMode
{
    private readonly ApplicationDbContext _db;
    private readonly ISubmissionEventHandler _submissionEventHandler;
    private readonly DynamicScoringCalculator _scoringCalculator;

    public GameModeType Type => GameModeType.Ctf;

    public CtfGameMode(
        ApplicationDbContext db,
        ISubmissionEventHandler submissionEventHandler,
        DynamicScoringCalculator scoringCalculator)
    {
        _db = db;
        _submissionEventHandler = submissionEventHandler;
        _scoringCalculator = scoringCalculator;
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
            return SubmissionResult.WrongFlag;
        }

        // Count existing correct solves for this challenge (excluding this team's new solve)
        // to determine dynamic score at time of solve
        var solveCount = await _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

        // solveCount is the count BEFORE this solve; after adding this submission it becomes solveCount+1
        var pointsAwarded = _scoringCalculator.Calculate(solveCount + 1, challenge.PointsConfig);

        // Check if this is first blood (no prior correct solves)
        var isFirstBlood = solveCount == 0;

        // Write ScoreEvent (points are derived from ScoreEvents, not stored in Submission)
        var scoreEvent = new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            EventType = "FlagSolved",
            PointsDelta = pointsAwarded,
            Reason = $"Solved challenge '{challenge.Title}'",
            Timestamp = DateTime.UtcNow
        };

        _db.ScoreEvents.Add(scoreEvent);
        await _db.SaveChangesAsync(cancellationToken);

        // Recalculate scores for all teams that solved this challenge (dynamic scoring)
        await RecalculateDynamicScoresAsync(context.CompetitionId, context.ChallengeId, challenge, cancellationToken);

        // Load team name for notification
        var team = await _db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == context.TeamId, cancellationToken);

        var teamName = team?.Name ?? context.TeamId.ToString();

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

    /// <summary>
    /// After a new solve, recalculate and update ScoreEvents for all teams that solved this challenge.
    /// Dynamic scoring means earlier solvers' points decrease as more teams solve.
    /// We add a correction ScoreEvent to adjust each prior solver's score.
    /// </summary>
    private async Task RecalculateDynamicScoresAsync(
        Guid competitionId,
        Guid challengeId,
        Challenge challenge,
        CancellationToken cancellationToken)
    {
        // Get all correct solves for this challenge, ordered by time
        var solves = await _db.Submissions
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.ChallengeId == challengeId &&
                s.IsCorrect)
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new { s.TeamId, s.SubmittedAt })
            .ToListAsync(cancellationToken);

        var totalSolves = solves.Count;
        var newPoints = _scoringCalculator.Calculate(totalSolves, challenge.PointsConfig);

        // For each prior solver, compute their current total ScoreEvent points for this challenge
        // and emit a correction so their final total equals newPoints.
        for (int i = 0; i < totalSolves; i++)
        {
            // The latest solver already received the correct points in ProcessSubmissionAsync
            if (i == totalSolves - 1)
                continue;

            var teamId = solves[i].TeamId;

            var currentTotal = await _db.ScoreEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(se =>
                    se.CompetitionId == competitionId &&
                    se.TeamId == teamId &&
                    se.ChallengeId == challengeId &&
                    (se.EventType == "FlagSolved" || se.EventType == "ScoreCorrection"))
                .SumAsync(se => (long)se.PointsDelta, cancellationToken);

            var delta = newPoints - (int)currentTotal;
            if (delta != 0)
            {
                var correction = new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = teamId,
                    ChallengeId = challengeId,
                    EventType = "ScoreCorrection",
                    PointsDelta = delta,
                    Reason = $"Dynamic score adjustment for challenge '{challenge.Title}'",
                    Timestamp = DateTime.UtcNow
                };
                _db.ScoreEvents.Add(correction);
            }
        }

        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync(cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// AWD game mode: handles flag-stealing submissions and round notifications.
/// </summary>
public class AwdGameMode : IGameMode
{
    private readonly ApplicationDbContext _db;
    private readonly IHubNotifierService _hubNotifier;
    private readonly IScoreSignalEmitter _scoreSignalEmitter;

    public GameModeType Type => GameModeType.Awd;

    public AwdGameMode(
        ApplicationDbContext db,
        IHubNotifierService hubNotifier,
        IScoreSignalEmitter scoreSignalEmitter)
    {
        _db = db;
        _hubNotifier = hubNotifier;
        _scoreSignalEmitter = scoreSignalEmitter;
    }

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called by AwdRoundEngine on each round tick. Notifies clients that a new round started.
    /// </summary>
    public async Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Configuration.TryGetValue("RoundNumber", out var roundStr) ||
            !int.TryParse(roundStr, out var roundNumber))
            return;

        await _hubNotifier.NotifyRoundStartedAsync(context.CompetitionId, roundNumber, cancellationToken);
    }

    /// <summary>
    /// Processes an AWD flag submission (flag stealing).
    /// Validates the flag, checks for self-attack and duplicates, records the attack, and emits a scoring signal.
    /// </summary>
    public async Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var competition = await _db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, cancellationToken);
        if (competition is null)
            return SubmissionResult.WrongFlag;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return SubmissionResult.CompetitionNotStarted;
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return SubmissionResult.CompetitionEnded;
        if (competition.Status == CompetitionStatus.Paused)
            return SubmissionResult.CompetitionPaused;

        // Find the AwdFlag matching the submitted content for this competition
        var flag = await _db.AwdFlags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.CompetitionId == context.CompetitionId
                                   && f.ChallengeId == context.ChallengeId
                                   && f.FlagContent == context.FlagContent, cancellationToken);

        if (flag is null)
            return SubmissionResult.WrongFlag;

        // Self-attack prevention
        if (flag.TeamId == context.TeamId)
            return SubmissionResult.WrongFlag;

        // Get the current (latest) round number
        var latestRound = await _db.AwdRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == context.CompetitionId)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRound is null)
            return SubmissionResult.WrongFlag;

        int currentRound = latestRound.RoundNumber;

        // Flag validity window check
        int validityRounds = competition.FlagValidityRounds ?? 2;
        int minValidRound = currentRound - validityRounds + 1;

        if (flag.RoundNumber < minValidRound)
            return SubmissionResult.WrongFlag;

        // Duplicate attack prevention: same attacker/victim/challenge/round
        var isDuplicate = await _db.AwdAttackRecords
            .IgnoreQueryFilters()
            .AnyAsync(a => a.CompetitionId == context.CompetitionId
                        && a.AttackerTeamId == context.TeamId
                        && a.VictimTeamId == flag.TeamId
                        && a.ChallengeId == flag.ChallengeId
                        && a.RoundNumber == flag.RoundNumber, cancellationToken);

        if (isDuplicate)
            return SubmissionResult.WrongFlag;

        // Record the attack
        _db.AwdAttackRecords.Add(new AwdAttackRecord
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            AttackerTeamId = context.TeamId,
            VictimTeamId = flag.TeamId,
            ChallengeId = flag.ChallengeId,
            RoundNumber = flag.RoundNumber,
            FlagContent = RedactSubmittedFlag(context.FlagContent),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _scoreSignalEmitter.EmitAsync(new ScoreSignalCreate(
            CompetitionId: context.CompetitionId,
            TeamId: context.TeamId,
            SignalType: ScoreSignalTypes.AttackAccepted,
            IdempotencyKey: $"awd:{flag.RoundNumber}:{context.TeamId:N}:{flag.TeamId:N}:{flag.ChallengeId:N}:attack",
            SubjectType: "challenge",
            SubjectId: flag.ChallengeId,
            ActorUserId: context.UserId,
            RoundNumber: flag.RoundNumber,
            PayloadJson: ScoringJson.Serialize(new { victimTeamId = flag.TeamId }),
            OccurredAt: DateTime.UtcNow), cancellationToken);

        // Broadcast attack log via SignalR — look up names for the notification
        var attackerTeam = await _db.Teams
            .IgnoreQueryFilters()
            .Where(t => t.Id == context.TeamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var victimTeam = await _db.Teams
            .IgnoreQueryFilters()
            .Where(t => t.Id == flag.TeamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var challenge = await _db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.Id == flag.ChallengeId)
            .Select(c => c.Title)
            .FirstOrDefaultAsync(cancellationToken);

        await _hubNotifier.NotifyAttackLogAsync(
            context.CompetitionId,
            context.TeamId, attackerTeam ?? context.TeamId.ToString(),
            flag.TeamId, victimTeam ?? flag.TeamId.ToString(),
            flag.ChallengeId, challenge ?? flag.ChallengeId.ToString(),
            flag.RoundNumber,
            cancellationToken);

        return SubmissionResult.Accepted;
    }

    private static string RedactSubmittedFlag(string submittedFlag)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(submittedFlag));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()};len:{submittedFlag.Length}";
    }
}

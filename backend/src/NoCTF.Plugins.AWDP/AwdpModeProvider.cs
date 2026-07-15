using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWDP;

public class AwdpModeProvider(
    ApplicationDbContext db,
    IAwdpPatchService patchService) : ICompetitionModeProvider, ICompetitionFileActionProvider
{
    private const string ChallengeStateView = "challenge-state";
    private const string SubmitPatchAction = "submit-patch";

    public string ModeKey => "awdp";

    public CompetitionCapabilityDescriptor GetCapabilities()
        => new(
            ModeKey,
            ["submit-flag", "submit-patch"],
            ["challenge-state", "patch-submissions"],
            ["round", "fixscript-validation"]);

    public bool CanHandleAction(string actionKey)
        => false;

    public Task<CompetitionActionResult> HandleActionAsync(
        CompetitionActionContext context,
        CancellationToken ct = default)
        => Task.FromResult(new CompetitionActionResult(false, "unsupported_action"));

    public bool CanHandleFileAction(string actionKey)
        => string.Equals(actionKey, SubmitPatchAction, StringComparison.OrdinalIgnoreCase);

    public async Task<CompetitionActionResult> HandleFileActionAsync(
        CompetitionFileActionContext context,
        CancellationToken ct = default)
    {
        if (!CanHandleFileAction(context.ActionKey))
            return new CompetitionActionResult(false, "unsupported_action");

        var result = await patchService.SubmitPatchAsync(
            context.CompetitionId,
            context.TeamId,
            context.ChallengeId,
            context.File,
            context.FileName,
            ct);

        if (!result.Success || result.SubmissionId is null)
        {
            return new CompetitionActionResult(false, result.Code, new
            {
                result.SubmissionId,
                result.DefenseAttempts,
                result.MaxDefenseAttempts
            });
        }

        return new CompetitionActionResult(true, result.Code, new
        {
            result.SubmissionId,
            result.DefenseAttempts,
            result.MaxDefenseAttempts
        });
    }

    public bool CanProvideView(string viewKey)
        => string.Equals(viewKey, ChallengeStateView, StringComparison.OrdinalIgnoreCase);

    public async Task<CompetitionViewResult> GetViewAsync(
        CompetitionViewContext context,
        CancellationToken ct = default)
    {
        if (!CanProvideView(context.ViewKey))
            throw new NotSupportedException($"AWDP view '{context.ViewKey}' is not implemented.");

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == context.CompetitionId, ct);
        var now = DateTime.UtcNow;
        var active = competition.Status == CompetitionStatus.Running &&
                     competition.StartTime <= now &&
                     competition.EndTime > now;

        var currentRound = await db.AwdpRounds
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.CompetitionId == context.CompetitionId)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(ct);

        if (context.TeamId is null)
        {
            return new CompetitionViewResult(ChallengeStateView, new
            {
                code = "no_team",
                competitionId = context.CompetitionId,
                currentRound = ToRoundDto(currentRound),
                serverTime = now,
                challenges = Array.Empty<object>()
            });
        }

        var teamId = context.TeamId.Value;
        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == context.CompetitionId && !c.IsDeleting)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        var activeChallengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        var states = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == teamId &&
                activeChallengeIds.Contains(s.ChallengeId))
            .ToListAsync(ct);
        var boxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(g =>
                g.CompetitionId == context.CompetitionId &&
                g.TeamId == teamId &&
                activeChallengeIds.Contains(g.ChallengeId))
            .ToListAsync(ct);
        var roundNumber = currentRound?.RoundNumber;
        var roundScores = roundNumber is null
            ? []
            : await db.AwdpRoundScores
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s =>
                    s.CompetitionId == context.CompetitionId &&
                    s.TeamId == teamId &&
                    activeChallengeIds.Contains(s.ChallengeId) &&
                    s.RoundNumber == roundNumber.Value)
                .ToListAsync(ct);

        var stateMap = states.ToDictionary(s => s.ChallengeId);
        var boxMap = boxes.ToDictionary(b => b.ChallengeId);
        var scoreMap = roundScores.ToDictionary(s => s.ChallengeId);
        var challengeStates = new List<object>();

        foreach (var challenge in challenges)
        {
            stateMap.TryGetValue(challenge.Id, out var state);
            boxMap.TryGetValue(challenge.Id, out var box);
            scoreMap.TryGetValue(challenge.Id, out var roundScore);
            var config = AwdpConfigResolver.Resolve(competition, challenge);
            var instanceStatus = ResolveInstanceStatus(box, now);
            var breakStatus = state?.BreakStatus ?? AwdpBreakStatus.BreakNotStarted;
            var fixStatus = state?.FixStatus ?? AwdpFixStatus.FixNotStarted;
            var serviceStatus = state?.ServiceStatus ?? AwdpServiceStatus.ServiceUnknown;
            var attackAttempts = state?.AttackAttempts ?? 0;
            var defenseAttempts = state?.DefenseAttempts ?? 0;
            var roundStart = currentRound?.StartTime ?? DateTime.MinValue;
            var attackRemaining = Math.Max(0, config.MaxAttackAttempts - attackAttempts);
            var defenseRemaining = Math.Max(0, config.MaxDefenseAttempts - defenseAttempts);
            var canSubmitFlag = active &&
                                instanceStatus == AwdpInstanceStatus.InstanceRunning &&
                                attackRemaining > 0 &&
                                (breakStatus != AwdpBreakStatus.BreakSuccess || config.AllowAttackAfterBreakSuccess);
            var canRequestDefense = active &&
                                    instanceStatus == AwdpInstanceStatus.InstanceRunning &&
                                    defenseRemaining > 0 &&
                                    (fixStatus != AwdpFixStatus.FixSuccess || config.AllowDefenseAfterFixSuccess);
            var attackDelta = roundScore?.AttackScoreDelta ??
                              (breakStatus == AwdpBreakStatus.BreakSuccess &&
                               IsEffectiveForRound(state?.BreakSucceededAt, roundStart)
                                  ? config.AttackScorePerRound
                                  : 0);
            var defenseDelta = roundScore?.DefenseScoreDelta ??
                               (fixStatus == AwdpFixStatus.FixSuccess &&
                                IsEffectiveForRound(state?.FixSucceededAt, roundStart)
                                   ? config.DefenseScorePerRound
                                   : 0);

            challengeStates.Add(new
            {
                challengeId = challenge.Id,
                instanceStatus = instanceStatus.ToString(),
                breakStatus = breakStatus.ToString(),
                fixStatus = AwdpPlayerDefenseResult.ToVisibleFixStatus(fixStatus).ToString(),
                serviceStatus = serviceStatus.ToString(),
                currentRoundAttackScore = attackDelta,
                currentRoundDefenseScore = defenseDelta,
                attackScorePerRound = config.AttackScorePerRound,
                defenseScorePerRound = config.DefenseScorePerRound,
                attackAttempts,
                defenseAttempts,
                maxAttackAttempts = config.MaxAttackAttempts,
                maxDefenseAttempts = config.MaxDefenseAttempts,
                remainingAttackAttempts = attackRemaining,
                remainingDefenseAttempts = defenseRemaining,
                canSubmitFlag,
                canRequestDefense,
                allowAttackAfterBreakSuccess = config.AllowAttackAfterBreakSuccess,
                allowDefenseAfterFixSuccess = config.AllowDefenseAfterFixSuccess,
                fixEntry = config.FixEntry,
                lastValidationDetail = AwdpPlayerDefenseResult.ToVisibleDetail(fixStatus),
                cooldownUntil = box?.LastInstanceActionAt?.Add(AwdpPatchService.ContainerOperationCooldown)
            });
        }

        return new CompetitionViewResult(ChallengeStateView, new
        {
            code = "ok",
            competitionId = context.CompetitionId,
            teamId,
            currentRound = ToRoundDto(currentRound),
            serverTime = now,
            challenges = challengeStates
        });
    }

    private static object? ToRoundDto(AwdpRound? round)
        => round is null
            ? null
            : new
            {
                roundNumber = round.RoundNumber,
                status = round.Status.ToString(),
                startTime = round.StartTime,
                endTime = round.EndTime
            };

    private static AwdpInstanceStatus ResolveInstanceStatus(AwdGameBox? box, DateTime now)
    {
        if (box?.ContainerInstanceId is null)
            return AwdpInstanceStatus.InstanceNotCreated;

        if (box.ExpiresAt is not null && box.ExpiresAt <= now)
            return AwdpInstanceStatus.InstanceExpired;

        return AwdpInstanceStatus.InstanceRunning;
    }

    private static bool IsEffectiveForRound(DateTime? succeededAt, DateTime roundStart)
        => succeededAt is null || succeededAt <= roundStart;
}

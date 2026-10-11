using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore : ILiveSoloWorkspaceReader
{
    public async Task<LiveSoloWorkspaceView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (await FindAsync(competitionId, matchId, actorId, staff, now, ct) is null) return null;
        if (!await InitializeResultsAsync(competitionId, matchId, ct)) return null;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var view = await FindAsync(competitionId, matchId, actorId, staff, now, ct); if (view is null) return null;
        var match = await MatchAsync(competitionId, matchId, ct); if (match is null) return null;
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == competitionId, ct);
        var round = match.CurrentRoundId is Guid roundId ? await db.LiveSoloRounds.AsNoTracking().Include(x => x.Pauses).Include(x => x.Questions)
            .SingleOrDefaultAsync(x => x.Id == roundId && x.MatchId == match.Id, ct) : null;
        if (round is not null) await SynchronizeCompetitionPausesAsync(round, competitionId, now, false, ct);
        var conditions = new List<LiveSoloStartCondition>();
        if (competition.Status != CompetitionStatus.Running) conditions.Add(LiveSoloStartCondition.CompetitionNotRunning);
        foreach (var slot in match.Slots)
        {
            if (slot.RosterLockedAt is null) conditions.Add(slot.Side == LiveSoloSide.Left ? LiveSoloStartCondition.LeftRosterUnlocked : LiveSoloStartCondition.RightRosterUnlocked);
            if (slot.ReadyConfirmedAt is null) conditions.Add(slot.Side == LiveSoloSide.Left ? LiveSoloStartCondition.LeftNotReady : LiveSoloStartCondition.RightNotReady);
        }
        if (round?.State != LiveSoloRoundState.Preparing) conditions.Add(LiveSoloStartCondition.NoPreparedRound);
        else
        {
            var first = round.Questions.OrderBy(x => x.Position).FirstOrDefault();
            if (first?.Readiness == LiveSoloQuestionReadiness.Failed) conditions.Add(LiveSoloStartCondition.EnvironmentFailed);
            else if (first?.Readiness != LiveSoloQuestionReadiness.Ready) conditions.Add(LiveSoloStartCondition.EnvironmentPreparing);
            if (round.PlatformStreamingEnabled && !await db.LiveSoloMediaSessions.AnyAsync(x => x.Id == match.CurrentMediaSessionId && x.State == LiveSoloMediaState.Ready, ct))
                conditions.Add(LiveSoloStartCondition.MediaRequired);
            if (round.SelectionKind != LiveSoloQuestionSelectionKind.StaffSpecified && !await PreparedSelectionCurrentAsync(match, round, now, ct))
                conditions.Add(LiveSoloStartCondition.SelectionChanged);
        }
        if (match.PendingCorrectionId is not null) conditions.Add(LiveSoloStartCondition.CorrectionPending);
        var allowed = new List<LiveSoloWorkspaceOperation>();
        var terminal = match.State is LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled || match.SupersededAt != null;
        var judge = staff && await authorizer.CanJudgeAsync(actorId, competitionId, ct);
        var manager = staff && await authorizer.CanModerateAsync(actorId, competitionId, ct);
        if (manager) allowed.Add(LiveSoloWorkspaceOperation.ManageQuestionPool);
        if (manager && match.State == LiveSoloMatchState.Completed && match.PendingCorrectionId is null && match.SupersededAt == null) allowed.Add(LiveSoloWorkspaceOperation.CorrectResult);
        if (!terminal && match.PendingCorrectionId is null)
        {
            if (judge)
            {
                if (competition.Status != CompetitionStatus.Finished && match.State == LiveSoloMatchState.Preparing
                    && (round is null || round.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled))
                    allowed.Add(LiveSoloWorkspaceOperation.PrepareRound);
                if (round?.State == LiveSoloRoundState.Preparing)
                {
                    allowed.Add(LiveSoloWorkspaceOperation.RetryPreparation);
                    if (conditions.Count == 0) allowed.Add(LiveSoloWorkspaceOperation.StartCountdown);
                }
                else if (round?.State is LiveSoloRoundState.Countdown or LiveSoloRoundState.Running
                    && round.Questions.Any(x => x.OpenedAt == null && x.Readiness == LiveSoloQuestionReadiness.Failed))
                    allowed.Add(LiveSoloWorkspaceOperation.RetryPreparation);
                if (round?.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)
                    allowed.Add(LiveSoloWorkspaceOperation.VoidRound);
                if (round?.State is LiveSoloRoundState.Countdown or LiveSoloRoundState.Running)
                    allowed.Add(match.State == LiveSoloMatchState.Paused ? LiveSoloWorkspaceOperation.Resume : LiveSoloWorkspaceOperation.Pause);
                if (match.Slots.All(x => x.TeamId != null && x.Resolved)) allowed.Add(LiveSoloWorkspaceOperation.ForfeitMatch);
            }
            if (!staff && await TeamAsync(competitionId, actorId, ct) is Guid team)
            {
                var slot = match.Slots.SingleOrDefault(x => x.TeamId == team);
                if (slot != null && match.State == LiveSoloMatchState.Preparing)
                {
                    if (slot.RosterLockedAt == null && await db.Teams.AnyAsync(x => x.Id == team && x.CaptainId == actorId, ct)) allowed.Add(LiveSoloWorkspaceOperation.LockRoster);
                    if (slot.RosterLockedAt != null && slot.ReadyConfirmedAt == null && match.Roster.Any(x => x.TeamId == team && x.UserId == actorId)) allowed.Add(LiveSoloWorkspaceOperation.ConfirmReady);
                }
                if (round?.State == LiveSoloRoundState.Running && match.State == LiveSoloMatchState.Running && competition.Status == CompetitionStatus.Running
                    && round.StartedAt is { } started && NoCTF.Application.LiveSolo.Rounds.LiveSoloActiveClock.Elapsed(started, now, round.Pauses) < TimeSpan.FromSeconds(round.LimitSeconds)
                    && match.Roster.Any(x => x.TeamId == team && x.UserId == actorId)) allowed.Add(LiveSoloWorkspaceOperation.SubmitFlag);
            }
        }
        var questions = !staff && round != null ? await QuestionsAsync(competitionId, matchId, round.Id, actorId, now, ct) ?? [] : [];
        // Build metadata inside this same database snapshot; body and file requests authorize separately.
        var teamId = await TeamAsync(competitionId, actorId, ct);
        var historyAllowed = staff || competition.Status == CompetitionStatus.Finished && teamId != null
            || teamId != null && match.Roster.Any(x => x.UserId == actorId && x.TeamId == teamId)
                && match.Slots.Any(x => x.TeamId == teamId && x.RosterLockedAt != null);
        var history = historyAllowed ? await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.OpenedAt != null)
            .Join(db.LiveSoloRounds.Where(x => x.MatchId == matchId), q => q.RoundId, r => r.Id, (q, r) => new { q, r })
            .OrderBy(x => x.r.Number).ThenBy(x => x.r.Replay).ThenBy(x => x.q.Position)
            .Select(x => new LiveSoloReviewQuestion(x.q.Id, x.r.Id, x.q.CompetitionChallengeId, x.r.Number, x.r.Replay, x.q.Position,
                x.q.OpenedContent != null ? x.q.OpenedContent.Title : null, x.q.OpenedAt, x.q.OpenedContent != null)).ToArrayAsync(ct) : [];
        var next = round?.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running
            ? round.Questions.Where(x => x.OpenedAt == null).OrderBy(x => x.Position).Select(x => (int?)x.OpenOffsetSeconds).FirstOrDefault() : null;
        return new(view, round != null ? Round(round, now) : null, allowed, conditions, questions, history, now, next, staff)
        { Resources = staff && round != null ? round.Questions.OrderBy(x => x.Position).Select(x => new LiveSoloPreparationView(x.Id,
            x.Position, x.OpenOffsetSeconds, x.OpenedAt, x.Readiness, x.PreparationFailure, x.EnvironmentRequired)).ToArray() : [] };
    }

    private async Task<bool> PreparedSelectionCurrentAsync(LiveSoloMatch match, LiveSoloRound round, DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await QuestionCandidatesAsync(match, now, ct);
        return candidates.Any(x => x.Id == round.QuestionGroupId && x.AutomaticEligible);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private async Task<bool> RescueRoundActiveAsync(LiveSoloMatch match, LiveSoloRound round, DateTimeOffset now, CancellationToken ct)
    {
        if (match.CurrentRoundId != round.Id || match.PendingCorrectionId != null || match.SupersededAt != null
            || match.State is not (LiveSoloMatchState.Running or LiveSoloMatchState.Paused) || round.State != LiveSoloRoundState.Running
            || round.StartedAt is not { } started || !await db.Competitions.AnyAsync(x => x.Id == match.CompetitionId
                && (x.Status == CompetitionStatus.Running || x.Status == CompetitionStatus.Paused), ct)
            || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == match.CompetitionId && x.Enabled, ct)) return false;
        await SynchronizeCompetitionPausesAsync(round, match.CompetitionId, now, true, ct);
        return LiveSoloActiveClock.Elapsed(started, now, round.Pauses) < TimeSpan.FromSeconds(round.LimitSeconds);
    }

    public Task<LiveSoloMatchResult> RequestRescueAsync(RequestLiveSoloRescue command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct)) return new LiveSoloMatchResult(null, LiveSoloFailure.Forbidden);
        var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        var teamId = await TeamAsync(command.CompetitionId, command.ActorId, ct);
        if (match is null) return new(null, LiveSoloFailure.NotFound);
        if (teamId is not Guid team || !match.Slots.Any(x => x.TeamId == team)) return new(null, LiveSoloFailure.Forbidden);
        if ((command.Reason?.Trim().Length ?? 0) > 4000
            || !await db.Teams.AnyAsync(x => x.Id == team && x.Members.Any(m => m.UserId == command.UserId), ct)
            || !await ActiveAsync(command.UserId, ct)) return new(null, LiveSoloFailure.InvalidConfiguration);
        if (match.Roster.Any(x => x.UserId == command.UserId)) return new(null, LiveSoloFailure.RosterLocked);
        var round = await db.LiveSoloRounds.Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == match.Id, ct);
        if (round is null || !await RescueRoundActiveAsync(match, round, command.Now, ct)) return new(null, LiveSoloFailure.NotReady);
        if (await db.Set<LiveSoloRescueRequest>().AnyAsync(x => x.RoundId == round.Id && x.TeamId == team && x.UserId == command.UserId && x.State == LiveSoloRescueState.Pending, ct))
            return new(await MapAsync(match, ct));
        db.Add(new LiveSoloRescueRequest { Id = Guid.CreateVersion7(command.Now), MatchId = match.Id, RoundId = round.Id, TeamId = team,
            ActorUserId = command.ActorId, UserId = command.UserId, Reason = command.Reason?.Trim() ?? "", RequestedAt = command.Now });
        match.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public Task<LiveSoloMatchResult> ResolveRescueAsync(ResolveLiveSoloRescue command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct))
            return new LiveSoloMatchResult(null, LiveSoloFailure.Forbidden);
        var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        var request = await db.Set<LiveSoloRescueRequest>().SingleOrDefaultAsync(x => x.Id == command.RequestId && x.MatchId == command.MatchId, ct);
        if (match is null || request is null) return new(null, LiveSoloFailure.NotFound);
        if (request.State != LiveSoloRescueState.Pending) return new(await MapAsync(match, ct));
        if ((command.Reason?.Trim().Length ?? 0) > 4000) return new(null, LiveSoloFailure.InvalidConfiguration);
        var round = await db.LiveSoloRounds.Include(x => x.Pauses).SingleAsync(x => x.Id == request.RoundId, ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (!await RescueRoundActiveAsync(match, round, now, ct)) return new(null, LiveSoloFailure.NotReady);
        if (command.Approve)
        {
            if (!await ActiveAsync(request.UserId, ct) || !await db.Teams.AnyAsync(x => x.Id == request.TeamId && !x.IsBanned
                && x.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved
                && x.Members.Any(m => m.UserId == request.UserId), ct)) return new(null, LiveSoloFailure.Forbidden);
            if (!match.Roster.Any(x => x.UserId == request.UserId))
            {
                match.Roster.Add(new() { MatchId = match.Id, TeamId = request.TeamId, UserId = request.UserId, ConfirmedAt = now });
                db.Add(new LiveSoloRoundRosterMember { RoundId = round.Id, TeamId = request.TeamId, UserId = request.UserId, JoinedAt = now });
                if (match.CurrentMediaSessionId is Guid sessionId)
                {
                    var session = await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleAsync(x => x.Id == sessionId, ct);
                    if (!session.Participants.Any(x => x.UserId == request.UserId)) session.Participants.Add(new() { UserId = request.UserId,
                        TeamId = request.TeamId, Side = match.Slots.Single(x => x.TeamId == request.TeamId).Side,
                        Identity = Guid.NewGuid().ToString("N"), ObservedAt = now });
                    await messages.PublishAsync(new RefreshLiveSoloMedia(session.Id, session.RoomIdentity));
                }
            }
        }
        request.State = command.Approve ? LiveSoloRescueState.Approved : LiveSoloRescueState.Rejected;
        request.JudgeUserId = command.ActorId; request.DecisionReason = command.Reason?.Trim() ?? ""; request.ResolvedAt = now;
        request.ConcurrencyStamp = Guid.NewGuid(); match.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    private async Task ExpireRoundRescuesAsync(LiveSoloMatch match, Guid roundId, DateTimeOffset now, CancellationToken ct)
    {
        var rescued = await db.Set<LiveSoloRescueRequest>().Where(x => x.RoundId == roundId && x.State == LiveSoloRescueState.Approved)
            .Select(x => x.UserId).ToArrayAsync(ct);
        var members = match.Roster.Where(x => rescued.Contains(x.UserId)).ToArray();
        if (members.Length == 0) return;
        db.RemoveRange(members);
        foreach (var member in members) match.Roster.Remove(member);
        await RetireMediaAsync(match, now, ct);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloMatchResult> LockRosterAsync(SetLiveSoloRoster command, CancellationToken ct) => await TransactionAsync(async () =>
    {
        var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
        if (match is null) return new LiveSoloMatchResult(null, LiveSoloFailure.NotFound);
        if (match.PendingCorrectionId is not null) return new(null, LiveSoloFailure.NotReady);
        if (match.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
        var slot = match.Slots.SingleOrDefault(x => x.TeamId == command.TeamId);
        if (slot is null || match.State != LiveSoloMatchState.Preparing || match.StartedAt is not null) return new(null, LiveSoloFailure.RosterLocked);
        var team = await db.Teams.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == command.TeamId && x.CompetitionId == command.CompetitionId, ct);
        if (team is null || team.IsBanned || !await ActiveAsync(command.ActorId, ct)
            || team.CaptainId != command.ActorId && !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new(null, LiveSoloFailure.Forbidden);
        var config = await db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(x => x.CompetitionId == command.CompetitionId, ct);
        if (command.UserIds.Count > config.MaximumRosterMembers || command.UserIds.Count == 0
            || command.UserIds.Any(id => !team.Members.Any(x => x.UserId == id))
            || await db.Users.CountAsync(x => command.UserIds.Contains(x.Id) && x.AccountStatus == UserAccountStatus.Active, ct) != command.UserIds.Count)
            return new(null, LiveSoloFailure.InvalidConfiguration);
        if (slot.RosterLockedAt is not null) return new(null, LiveSoloFailure.RosterLocked);
        var members = command.UserIds.Select(id => new LiveSoloRosterMember { MatchId = match.Id, TeamId = team.Id, UserId = id, ConfirmedAt = command.Now }).ToArray();
        match.Roster.AddRange(members); db.Set<LiveSoloRosterMember>().AddRange(members);
        slot.RosterLockedAt = command.Now; slot.ReadyConfirmedAt = null; match.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloMatchResult> ConfirmReadyAsync(Guid competitionId, Guid matchId, Guid actorId, Guid expectedStamp, DateTimeOffset now, CancellationToken ct) =>
        await TransactionAsync(async () =>
        {
            var match = await MatchAsync(competitionId, matchId, ct); var team = await TeamAsync(competitionId, actorId, ct);
            if (match is null) return new LiveSoloMatchResult(null, LiveSoloFailure.NotFound);
            if (match.PendingCorrectionId is not null) return new(null, LiveSoloFailure.NotReady);
            if (match.ConcurrencyStamp != expectedStamp) return new(null, LiveSoloFailure.Conflict);
            var slot = match.Slots.SingleOrDefault(x => x.TeamId == team && team is not null);
            if (!await ActiveAsync(actorId, ct) || slot is null || slot.RosterLockedAt is null || !match.Roster.Any(x => x.UserId == actorId)) return new(null, LiveSoloFailure.Forbidden);
            if (match.State != LiveSoloMatchState.Preparing) return new(null, LiveSoloFailure.NotReady);
            slot.ReadyConfirmedAt = now; match.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
}

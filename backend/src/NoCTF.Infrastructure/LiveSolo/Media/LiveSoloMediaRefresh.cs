using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloMediaStore
{
    public async Task RefreshAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null || session.State == LiveSoloMediaState.Stopped) return;
        if (session.State is LiveSoloMediaState.Stopping or LiveSoloMediaState.Rotating)
        { await StopSessionAsync(session, ct); return; }
        var match = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Roster).SingleOrDefaultAsync(x => x.Id == session.MatchId, ct);
        if (match is null || !LiveSoloMediaPolicy.Active(match.State) || match.CurrentMediaSessionId != session.Id
            || !await AvailableAsync(match.CompetitionId, ct) || !await RosterEligibleAsync(match, ct))
        {
            await StopSessionAsync(session, ct); return;
        }
        if (session.State == LiveSoloMediaState.Preparing)
        {
            foreach (var previous in await db.LiveSoloMediaSessions.Where(x => x.MatchId == match.Id && x.Id != session.Id
                && x.State != LiveSoloMediaState.Stopped).ToArrayAsync(ct)) await StopSessionAsync(previous, ct);
            await gateway.CreateRoomAsync(session.RoomIdentity, ct);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await db.Entry(match).ReloadAsync(ct); await db.Entry(session).ReloadAsync(ct);
            if (match.CurrentMediaSessionId == session.Id && LiveSoloMediaPolicy.Active(match.State)
                && session.State == LiveSoloMediaState.Preparing && await AvailableAsync(match.CompetitionId, ct)
                && await RosterEligibleAsync(match, ct))
            { session.State = LiveSoloMediaState.Ready; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
            return;
        }
        var grants = await db.LiveSoloMediaGrants.AsNoTracking().Where(x => x.MediaSessionId == session.Id && x.RevokedAt == null).ToArrayAsync(ct);
        IReadOnlyDictionary<string, MfaFailure?> decisions;
        try
        {
            decisions = await authentication.ValidateContextsAsync(grants.Select(grant =>
                new MfaContextValidationRequest(grant.Id.ToString("N"), grant.UserId, grant.TokenVersion, Proof(grant))).ToArray(), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { await StopSessionAsync(session, ct); return; }
        var invalid = false;
        foreach (var grant in grants)
        {
            if (!decisions.TryGetValue(grant.Id.ToString("N"), out var failure) || failure is not null
                || !await EligibleAsync(match, grant.UserId, grant.Role != LiveSoloMediaGrantRole.Publisher, ct)
                || grant.Role == LiveSoloMediaGrantRole.Director && !await authorizer.CanModerateAsync(grant.UserId, match.CompetitionId, ct))
            { invalid = true; break; }
        }
        if (invalid)
        {
            await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                await db.Entry(session).ReloadAsync(ct); await db.Entry(match).ReloadAsync(ct);
                if (match.CurrentMediaSessionId != session.Id || session.State != LiveSoloMediaState.Ready) return;
                session.State = LiveSoloMediaState.Rotating; match.CurrentMediaSessionId = null; match.ConcurrencyStamp = Guid.NewGuid();
                var active = await db.LiveSoloMediaGrants.Where(x => x.MediaSessionId == session.Id && x.RevokedAt == null).ToArrayAsync(ct);
                foreach (var grant in active) grant.RevokedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            }
            await StopSessionAsync(session, ct);
            // The replacement room needs freshly authorized joins; expired self-hosted tokens cannot recreate the deleted room.
            return;
        }
        var observed = await gateway.ObserveAsync(session.RoomIdentity, ct);
        if (!observed.Exists)
        {
            // A vanished room is retired, never resurrected with the same identity and still-valid old tokens.
            await StopSessionAsync(session, ct);
            return;
        }
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await db.Entry(session).ReloadAsync(ct);
            if (session.State != LiveSoloMediaState.Ready) return;
            foreach (var member in session.Participants)
            {
                var screen = observed.Screens.SingleOrDefault(x => x.Identity == member.Identity);
                member.ScreenState = screen?.State ?? LiveSoloScreenState.Disconnected;
                member.ScreenTrackId = screen?.TrackId; member.ObservedAt = clock.GetUtcNow();
            }
            session.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        var allowed = session.Participants.Select(x => x.Identity).Concat(grants.Select(x => x.Identity)).ToHashSet(StringComparer.Ordinal);
        foreach (var stranger in observed.Screens.Where(x => !allowed.Contains(x.Identity)))
            await gateway.DisconnectAsync(session.RoomIdentity, stranger.Identity, ct);
    }
    private async Task StopSessionAsync(LiveSoloMediaSession session, CancellationToken ct)
    {
        if (session.State == LiveSoloMediaState.Stopped) return;
        session.State = LiveSoloMediaState.Stopping;
        var match = await db.LiveSoloMatches.SingleOrDefaultAsync(x => x.Id == session.MatchId, ct);
        if (match?.CurrentMediaSessionId == session.Id)
        { match.CurrentMediaSessionId = null; match.ConcurrencyStamp = Guid.NewGuid(); }
        foreach (var grant in await db.LiveSoloMediaGrants.Where(x => x.MediaSessionId == session.Id && x.RevokedAt == null).ToArrayAsync(ct))
            grant.RevokedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await gateway.StopRoomAsync(session.RoomIdentity, ct);
        session.State = LiveSoloMediaState.Stopped; session.StoppedAt = clock.GetUtcNow();
        foreach (var member in session.Participants) member.ScreenState = LiveSoloScreenState.Disconnected;
        await db.SaveChangesAsync(ct);
    }
}

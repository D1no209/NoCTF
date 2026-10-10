using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloMediaStore
{
    public async Task RefreshAsync(RefreshLiveSoloMedia command, CancellationToken ct)
    {
        if (!Guid.TryParseExact(command.RoomIdentity, "N", out _)) throw new InvalidOperationException("Invalid media room identity.");
        try { await RefreshCoreAsync(command, ct); }
        catch(Exception exception) when(IsTransactionConflict(exception))
        {messages.DiscardPendingMessages();throw;}
        catch (Exception exception) when (!ct.IsCancellationRequested && TransactionFailureClassifier.IsRetryable(exception))
        {
            messages.DiscardPendingMessages();
            // The durable scheduler carries an opaque immutable room binding. DB loss must not leave an existing raw room authorized.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await gateway.StopRoomAsync(command.RoomIdentity, cleanup.Token);
            throw;
        }
        catch { messages.DiscardPendingMessages(); throw; }
    }
    private static bool IsTransactionConflict(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if(current is DbUpdateConcurrencyException or System.Data.Common.DbException {SqlState:"40001" or "40P01"})return true;
        return false;
    }
    private async Task RefreshCoreAsync(RefreshLiveSoloMedia command, CancellationToken ct)
    {
        var session = await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == command.SessionId, ct);
        if (session?.RoomIdentity != command.RoomIdentity) { await gateway.StopRoomAsync(command.RoomIdentity, ct); return; }
        if (session.State == LiveSoloMediaState.Stopped) return;
        if (session.State is LiveSoloMediaState.Stopping or LiveSoloMediaState.Rotating)
        { await StopSessionAsync(session, ct); return; }
        var match = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Roster).SingleOrDefaultAsync(x => x.Id == session.MatchId, ct);
        if (match is null || !LiveSoloMediaPolicy.Active(match.State) || match.CurrentMediaSessionId != session.Id
            || !await AvailableAsync(match, ct) || !await RosterEligibleAsync(match, ct))
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
                && session.State == LiveSoloMediaState.Preparing && await AvailableAsync(match, ct)
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
            await StopSessionAsync(session, ct, LiveSoloMediaAlertKind.RoomUnavailable);
            return;
        }
        await PersistObservationAsync(session.Id,match.Id,observed,ct);
        await messages.FlushCommittedMessagesAsync();
        var allowed = session.Participants.Select(x => x.Identity).Concat(grants.Select(x => x.Identity)).ToHashSet(StringComparer.Ordinal);
        foreach (var stranger in observed.Screens.Where(x => !x.IsRecorder && !allowed.Contains(x.Identity)))
            await gateway.DisconnectAsync(session.RoomIdentity, stranger.Identity, ct);
        if (captures is not null) await captures.EnsureAsync(session.Id, ct);
    }
    private async Task PersistObservationAsync(Guid sessionId,Guid matchId,LiveSoloRoomObservation observed,CancellationToken ct)
    {
        for(var attempt=0;;attempt++)
        {
            try
            {
                await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
                var session=await db.LiveSoloMediaSessions.Include(x=>x.Participants).SingleAsync(x=>x.Id==sessionId,ct);
                await db.Entry(session).ReloadAsync(ct);
                var match=await db.LiveSoloMatches.SingleAsync(x=>x.Id==matchId,ct);
                if(session.State!=LiveSoloMediaState.Ready||match.CurrentMediaSessionId!=session.Id)return;
                var previous=await db.Set<LiveSoloMediaParticipant>().AsNoTracking().Where(x=>x.MediaSessionId==session.Id)
                    .Select(x=>new{x.UserId,x.ScreenState,x.ScreenTrackId}).ToDictionaryAsync(x=>x.UserId,ct);
                var changed=false;
                foreach(var member in session.Participants)
                {
                    var screen=observed.Screens.SingleOrDefault(x=>x.Identity==member.Identity);
                    var state=screen?.State??LiveSoloScreenState.Disconnected;var before=previous[member.UserId];
                    if(before.ScreenState==LiveSoloScreenState.Sharing&&state!=LiveSoloScreenState.Sharing)
                        await AlertAsync(match,session,LiveSoloMediaAlertKind.ScreenInterrupted,state,member,ct);
                    changed|=before.ScreenState!=state||before.ScreenTrackId!=screen?.TrackId;
                    member.ScreenState=state;member.ScreenTrackId=screen?.TrackId;member.ObservedAt=clock.GetUtcNow();
                }
                if(changed){await MediaChangedAsync(match);session.ConcurrencyStamp=Guid.NewGuid();}
                await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return;
            }
            catch(Exception ex) when(attempt<2&&IsTransactionConflict(ex))
            {messages.DiscardPendingMessages();db.ChangeTracker.Clear();}
        }
    }
    private async Task StopSessionAsync(LiveSoloMediaSession session, CancellationToken ct, LiveSoloMediaAlertKind reason = LiveSoloMediaAlertKind.AuthorizationChanged)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            await db.Entry(session).ReloadAsync(ct);
            if (session.State == LiveSoloMediaState.Stopped) return;
            var match = await db.LiveSoloMatches.SingleOrDefaultAsync(x => x.Id == session.MatchId, ct);
            if (match is not null && LiveSoloMediaPolicy.Active(match.State)
                && (match.CurrentMediaSessionId == session.Id || session.State == LiveSoloMediaState.Rotating)
                && session.State is LiveSoloMediaState.Ready or LiveSoloMediaState.Rotating
                && await db.Set<LiveSoloMediaParticipant>().AnyAsync(x => x.MediaSessionId == session.Id && x.ScreenState == LiveSoloScreenState.Sharing, ct))
                await AlertAsync(match, session, reason, null, null, ct);
            session.State = LiveSoloMediaState.Stopping;
            if (match?.CurrentMediaSessionId == session.Id)
            { match.CurrentMediaSessionId = null; match.ConcurrencyStamp = Guid.NewGuid(); }
            foreach (var grant in await db.LiveSoloMediaGrants.Where(x => x.MediaSessionId == session.Id && x.RevokedAt == null).ToArrayAsync(ct)) grant.RevokedAt = clock.GetUtcNow();
            if (match is not null) await MediaChangedAsync(match);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await gateway.StopRoomAsync(session.RoomIdentity, ct);
        session.State = LiveSoloMediaState.Stopped; session.StoppedAt = clock.GetUtcNow();
        foreach (var member in session.Participants) member.ScreenState = LiveSoloScreenState.Disconnected;
        await db.SaveChangesAsync(ct);
        await messages.FlushCommittedMessagesAsync();
    }
}

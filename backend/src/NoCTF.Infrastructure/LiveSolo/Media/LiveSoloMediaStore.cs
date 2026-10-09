using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloMediaStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
    IMfaAuthenticationStore authentication, ILiveSoloMediaGateway gateway, NoCTF.Application.Messaging.IPostCommitMessagePublisher messages,
    TimeProvider clock, ILiveSoloCaptureStore? captures = null) : ILiveSoloMediaStore
{
    private async Task<LiveSoloMediaView> ViewAsync(LiveSoloMediaSession session, CancellationToken ct)
    {
        var ids = session.Participants.Select(x => x.UserId).ToArray();
        var names = await db.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName, ct);
        return LiveSoloMediaPolicy.View(session, names);
    }
    private async Task<bool> AvailableAsync(Guid competitionId, CancellationToken ct) =>
        await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == competitionId && x.Enabled, ct)
        && await db.Competitions.AnyAsync(x => x.Id == competitionId && (x.Status == CompetitionStatus.Running
            || x.Status == CompetitionStatus.Paused || x.Status == CompetitionStatus.Published || x.Status == CompetitionStatus.Visible), ct);

    private async Task<bool> RosterEligibleAsync(LiveSoloMatch match, CancellationToken ct)
    {
        var teams = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var members = match.Roster.Select(x => x.UserId).ToArray();
        return teams.Length == 2 && members.Length > 0
            && await db.Teams.CountAsync(x => teams.Contains(x.Id) && x.CompetitionId == match.CompetitionId
                && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved, ct) == 2
            && await db.Users.CountAsync(x => members.Contains(x.Id) && x.AccountStatus == UserAccountStatus.Active, ct) == members.Length
            && await db.Set<LiveSoloRosterMember>().Where(x => x.MatchId == match.Id)
                .Join(db.Set<TeamMember>(), x => new { x.TeamId, x.UserId }, x => new { x.TeamId, x.UserId }, (roster, _) => roster)
                .CountAsync(ct) == members.Length;
    }

    // Conflict responses require a fresh revision. External media operations are never replayed by this wrapper.
    private async Task<LiveSoloMediaResult> WithConflictAsync(Func<Task<LiveSoloMediaResult>> work)
    {
        try { return await work(); }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(ex))
        { db.ChangeTracker.Clear(); return new(null, Failure: LiveSoloMediaFailure.InvalidGeneration); }
    }
    private async Task<bool> EligibleAsync(LiveSoloMatch match, Guid actor, bool staff, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.AccountStatus == UserAccountStatus.Active, ct)) return false;
        if (staff) return await authorizer.CanObserveAsync(actor, match.CompetitionId, ct);
        return match.Roster.Any(x => x.UserId == actor) && await db.Teams.AnyAsync(x => x.CompetitionId == match.CompetitionId
            && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved && x.Members.Any(m => m.UserId == actor)
            && match.Slots.Select(s => s.TeamId).Contains(x.Id), ct);
    }
    public async Task<LiveSoloMediaView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        var match = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster)
            .SingleOrDefaultAsync(x => x.Id == matchId && x.CompetitionId == competitionId, ct);
        if (match is null || !await EligibleAsync(match, actorId, false, ct) && !await EligibleAsync(match, actorId, true, ct)) return null;
        var session = await db.LiveSoloMediaSessions.AsNoTracking().Include(x => x.Participants)
            .SingleOrDefaultAsync(x => x.Id == match.CurrentMediaSessionId, ct);
        return session is null ? null : await ViewAsync(session, ct);
    }
    public Task<LiveSoloMediaResult> PrepareAsync(PrepareLiveSoloMedia command, CancellationToken ct) =>
        WithConflictAsync(() => PrepareCoreAsync(command, ct));
    private async Task<LiveSoloMediaResult> PrepareCoreAsync(PrepareLiveSoloMedia command, CancellationToken ct)
    {
        var candidate = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster)
            .SingleOrDefaultAsync(x => x.Id == command.MatchId && x.CompetitionId == command.CompetitionId, ct);
        if (candidate is null || !await EligibleAsync(candidate, command.ActorId, false, ct)
            && (!await EligibleAsync(candidate, command.ActorId, true, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)))
            return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
        var readiness = await gateway.CheckAsync(ct);
        if (!readiness.Configured) return new(null, Failure: LiveSoloMediaFailure.Unconfigured);
        if (!readiness.Available) return new(null, Failure: LiveSoloMediaFailure.Unavailable);
        LiveSoloMediaSession session;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var match = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Roster).SingleOrDefaultAsync(
                x => x.Id == command.MatchId && x.CompetitionId == command.CompetitionId, ct);
            if (match is null || !await EligibleAsync(match, command.ActorId, false, ct)
                && (!await EligibleAsync(match, command.ActorId, true, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)))
                return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
            if (match.ConcurrencyStamp != command.ExpectedMatchStamp || !LiveSoloMediaPolicy.MayPrepare(match))
                return new(null, Failure: LiveSoloMediaFailure.InvalidGeneration);
            var config = await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().SingleAsync(x => x.CompetitionId == command.CompetitionId, ct);
            if (!await AvailableAsync(command.CompetitionId, ct) || !await RosterEligibleAsync(match, ct))
                return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
            var current = match.CurrentMediaSessionId is Guid id ? await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleAsync(x => x.Id == id, ct) : null;
            if (current is { State: LiveSoloMediaState.Ready }) return new(await ViewAsync(current, ct));
            if (current is { State: LiveSoloMediaState.Preparing }) session = current;
            else
            {
                session = new() { Id = Guid.CreateVersion7(clock.GetUtcNow()), MatchId = match.Id, Generation = Guid.NewGuid(),
                    RoomIdentity = Guid.NewGuid().ToString("N"), State = LiveSoloMediaState.Preparing, CreatedAt = clock.GetUtcNow(),
                    ParticipantsMayViewOpponents = config.ParticipantsMayViewOpponents, RecordingEnabled = config.RecordingEnabled,
                    RecordingRetentionDays = config.RecordingRetentionDays,
                    PublicDelaySeconds = config.PublicDelaySeconds, Participants = match.Roster.Select(member => new LiveSoloMediaParticipant
                    { UserId = member.UserId, TeamId = member.TeamId, Side = match.Slots.Single(x => x.TeamId == member.TeamId).Side,
                        Identity = Guid.NewGuid().ToString("N"), ObservedAt = clock.GetUtcNow() }).ToList() };
                db.LiveSoloMediaSessions.Add(session); match.CurrentMediaSessionId = session.Id; match.ConcurrencyStamp = Guid.NewGuid();
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        // No external operation is retried with a database transaction. Preparing is recoverable after a lost wakeup.
        await RefreshAsync(new(session.Id, session.RoomIdentity), ct);
        await db.Entry(session).ReloadAsync(ct);
        return session.State == LiveSoloMediaState.Ready ? new(await ViewAsync(session, ct))
            : new(null, Failure: LiveSoloMediaFailure.InvalidGeneration);
    }
    public Task<LiveSoloMediaResult> JoinAsync(JoinLiveSoloMedia command, CancellationToken ct) =>
        WithConflictAsync(() => JoinCoreAsync(command, ct));
    private async Task<LiveSoloMediaResult> JoinCoreAsync(JoinLiveSoloMedia command, CancellationToken ct)
    {
        if (command.Authentication is not { IsWellFormed: true }
            || await authentication.ValidateContextAsync(command.ActorId, command.TokenVersion, command.Authentication, ct) is not null)
            return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
        LiveSoloMediaSession session; string identity;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var match = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Roster).SingleOrDefaultAsync(
                x => x.Id == command.MatchId && x.CompetitionId == command.CompetitionId, ct);
            if (match is null || !LiveSoloMediaPolicy.Active(match.State)
                || !await AvailableAsync(command.CompetitionId, ct) || !await RosterEligibleAsync(match, ct)
                || !await EligibleAsync(match, command.ActorId, command.Role != LiveSoloMediaRole.Publisher, ct)
                || command.Role == LiveSoloMediaRole.Director && !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct))
                return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
            var current = await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == match.CurrentMediaSessionId, ct);
            if (current is null || current.Generation != command.Generation || current.State != LiveSoloMediaState.Ready)
                return new(null, Failure: LiveSoloMediaFailure.InvalidGeneration);
            session = current;
            var role = command.Role switch { LiveSoloMediaRole.Publisher => LiveSoloMediaGrantRole.Publisher,
                LiveSoloMediaRole.Judge => LiveSoloMediaGrantRole.Judge, _ => LiveSoloMediaGrantRole.Director };
            var grant = await db.LiveSoloMediaGrants.SingleOrDefaultAsync(x => x.MediaSessionId == session.Id && x.UserId == command.ActorId && x.Role == role, ct);
            if (grant?.RevokedAt is not null) return new(null, Failure: LiveSoloMediaFailure.InvalidGeneration);
            identity = command.Role == LiveSoloMediaRole.Publisher ? session.Participants.Single(x => x.UserId == command.ActorId).Identity
                : grant?.Identity ?? Guid.NewGuid().ToString("N");
            grant ??= new() { Id = Guid.CreateVersion7(clock.GetUtcNow()), MediaSessionId = session.Id, UserId = command.ActorId, Role = role, Identity = identity };
            if (db.Entry(grant).State == EntityState.Detached) db.LiveSoloMediaGrants.Add(grant);
            var proof = command.Authentication!;
            grant.TokenVersion = command.TokenVersion; grant.AuthenticationMethod = proof.Method; grant.AuthenticatedAt = proof.AuthenticatedAt;
            grant.MfaSource = proof.MfaSource; grant.MfaAuthenticatedAt = proof.MfaAuthenticatedAt; grant.MfaCredentialId = proof.CredentialId;
            grant.ProviderId = proof.ProviderId; grant.TrustPolicyId = proof.TrustPolicyId; grant.PrimaryCredentialId = proof.PrimaryCredentialId;
            grant.IssuedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        var token = await gateway.AuthorizeAsync(new(session.Id, session.Generation, session.RoomIdentity, identity, command.Role,
            command.Role != LiveSoloMediaRole.Publisher || session.ParticipantsMayViewOpponents, clock.GetUtcNow().AddMinutes(1)), ct);
        var stillCurrent = await db.LiveSoloMatches.AsNoTracking().AnyAsync(x => x.Id == command.MatchId && x.CurrentMediaSessionId == session.Id, ct);
        if (!stillCurrent || !await db.LiveSoloMediaSessions.AsNoTracking().AnyAsync(x => x.Id == session.Id && x.State == LiveSoloMediaState.Ready, ct))
            return new(null, Failure: LiveSoloMediaFailure.InvalidGeneration);
        var latestMatch = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster).SingleAsync(x => x.Id == command.MatchId, ct);
        if (!LiveSoloMediaPolicy.Active(latestMatch.State) || !await AvailableAsync(command.CompetitionId, ct)
            || !await RosterEligibleAsync(latestMatch, ct) || !await EligibleAsync(latestMatch, command.ActorId, command.Role != LiveSoloMediaRole.Publisher, ct)
            || command.Role == LiveSoloMediaRole.Director && !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)
            || await authentication.ValidateContextAsync(command.ActorId, command.TokenVersion, command.Authentication, ct) is not null)
            return new(null, Failure: LiveSoloMediaFailure.Unauthorized);
        return new(await ViewAsync(session, ct), token);
    }
    private static AuthenticationContext Proof(LiveSoloMediaGrant grant) => new(grant.AuthenticationMethod, grant.AuthenticatedAt, grant.MfaSource,
        grant.MfaAuthenticatedAt, grant.MfaCredentialId, grant.ProviderId, grant.TrustPolicyId, grant.PrimaryCredentialId);
}

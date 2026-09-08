using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.PublicAccess;

/// <summary>Serializes the short local lease activation with policy updates and Runtime stops.</summary>
public sealed class PublicGatewayLeaseGuard(NoCtfDbContext db, TimeProvider clock)
{
    public async Task RenewAsync(Guid runtimeId, string runnerId, PublicGatewayPolicy expected,
        Func<DateTimeOffset, CancellationToken, Task> writeLease, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Always settings -> individual Runtime; no remote FRP connection is awaited under these locks.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM platform_settings WHERE id = 1 FOR SHARE", ct);
        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var policy = new PublicGatewayPolicy(settings.PublicGatewayEnabled, settings.PublicGatewayConnectorId, settings.PublicGatewayOrigin,
            settings.PublicGatewayDirectOrigins, settings.PublicGatewayRuntimeHost, settings.PublicGatewayDirectHostOverride, settings.PublicGatewayMaxPorts);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM runtime_instances WHERE id = {runtimeId} FOR SHARE", ct);
        var runtime = await db.RuntimeInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runtimeId, ct);
        if (!policy.Enabled || policy.Fingerprint() != expected.Fingerprint() || runtime?.State != RuntimeState.Running
            || runtime.RunnerId != runnerId || runtime.ExpiresAt <= clock.GetUtcNow())
            throw new InvalidOperationException("Publication eligibility changed before activation.");
        // Renewals run independently of slow publication work. Recheck participant/publication/lifecycle
        // eligibility here, rather than retaining the last reconciliation's approval indefinitely.
        if (runtime.RuntimeProvider != RuntimeProvider.Docker || runtime.RuntimeKind != RuntimeKind.Container
            || !await db.Teams.AnyAsync(team => team.Id == runtime.TeamId && team.CompetitionId == runtime.CompetitionId
                && team.DeletedAt == null && !team.IsBanned && team.RegistrationStatus == TeamRegistrationStatus.Approved, ct)
            || !await db.CompetitionChallenges.AnyAsync(challenge => challenge.Id == runtime.CompetitionChallengeId
                && challenge.CompetitionId == runtime.CompetitionId && challenge.IsPublished, ct)
            || !await db.Competitions.AnyAsync(competition => competition.Id == runtime.CompetitionId
                && (competition.Mode == GameMode.Ctf && (runtime.Purpose == RuntimePurpose.Player || runtime.Purpose == RuntimePurpose.Practice)
                    || competition.Mode == GameMode.Awdp && (runtime.Purpose == RuntimePurpose.AwdpAttack || runtime.Purpose == RuntimePurpose.Practice))
                && (runtime.Purpose == RuntimePurpose.Practice
                    ? competition.Status == CompetitionStatus.Finished && competition.PracticeModeEnabled
                    : competition.Status == CompetitionStatus.Running || competition.Status == CompetitionStatus.Paused), ct))
            throw new InvalidOperationException("Participant or competition is no longer eligible for public access.");
        using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
        local.CancelAfter(TimeSpan.FromSeconds(1));
        var expires = clock.GetUtcNow().AddSeconds(9);
        if (runtime.ExpiresAt is { } until && until < expires) expires = until;
        await writeLease(expires, local.Token);
        await transaction.CommitAsync(ct);
    }
}

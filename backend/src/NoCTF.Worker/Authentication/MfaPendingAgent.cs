using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker.Authentication;

public sealed class MfaPendingAgent(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MfaPendingAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var pending = await db.MfaChallenges.AsNoTracking().Where(value => value.MailState == MfaMailState.Pending
                    || value.MailState == MfaMailState.Sending && value.MailAttemptedAt <= clock.GetUtcNow().AddMinutes(-5)).OrderBy(value => value.CreatedAt).Take(100).Select(value => value.Id).ToArrayAsync(stoppingToken);
                var publisher = scope.ServiceProvider.GetRequiredService<IPostCommitMessagePublisher>();
                foreach (var id in pending) await publisher.PublishAsync(new SendMfaMail(id));
                var expiredSecrets = await db.MfaChallenges.Where(value => value.ExpiresAt <= clock.GetUtcNow()
                    && (value.PendingSecretCiphertext != null || value.RecoveryGrantCiphertext != null)).Take(500).ToArrayAsync(stoppingToken);
                foreach (var challenge in expiredSecrets)
                {
                    challenge.PendingSecretCiphertext = null;
                    challenge.RecoveryGrantCiphertext = null;
                    if (challenge.Purpose == MfaChallengePurpose.RecoveryGrant) challenge.MailState = MfaMailState.Sent;
                    if (challenge.State is MfaChallengeState.Pending or MfaChallengeState.Verified) challenge.State = MfaChallengeState.Failed;
                }
                var expired = await db.MfaChallenges.Where(value => value.ExpiresAt < clock.GetUtcNow().AddDays(-1) && value.MailState != MfaMailState.Pending && value.MailState != MfaMailState.Sending).Take(500).ToArrayAsync(stoppingToken);
                var expiredPasskeys = await db.PasskeyCeremonies.Where(value => value.ExpiresAt <= clock.GetUtcNow()
                    && value.State == NoCTF.Domain.Identity.Passkeys.PasskeyCeremonyState.Pending).Take(500).ToArrayAsync(stoppingToken);
                foreach (var ceremony in expiredPasskeys) { ceremony.State = NoCTF.Domain.Identity.Passkeys.PasskeyCeremonyState.Failed; ceremony.ProtectedProtocolState = []; }
                var stalePasskeys = await db.PasskeyCeremonies.Where(value => value.ExpiresAt < clock.GetUtcNow().AddDays(-1)).Take(500).ToArrayAsync(stoppingToken);
                db.PasskeyCeremonies.RemoveRange(stalePasskeys);
                db.MfaChallenges.RemoveRange(expired); await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(exception, "MFA pending work will be retried."); }
        }
    }
}

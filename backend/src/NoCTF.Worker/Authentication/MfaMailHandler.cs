using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker.Authentication;

public sealed class MfaMailHandler(NoCtfDbContext db, PlatformSecretProtector protector, IMfaEmailDelivery delivery, TimeProvider clock)
{
    public async Task Handle(SendMfaMail message, CancellationToken ct)
    {
        var challenge = await db.MfaChallenges.SingleOrDefaultAsync(value => value.Id == message.ChallengeId, ct);
        if (challenge is null || challenge.MailState != MfaMailState.Pending) return;
        if (challenge.Purpose == MfaChallengePurpose.RecoveryGrant)
        {
            if (challenge.State != MfaChallengeState.Pending || challenge.ExpiresAt <= clock.GetUtcNow() || challenge.RecoveryGrantCiphertext is null)
            { challenge.MailState = MfaMailState.Sent; challenge.RecoveryGrantCiphertext = null; await db.SaveChangesAsync(ct); return; }
            var token = protector.Unprotect(challenge.RecoveryGrantCiphertext, PlatformSecretPurpose.MfaRecoveryGrant, challenge.UserId, challenge.Id);
            var state = await delivery.SendRecoveryAsync(challenge.UserId, token, ct);
            if (state == MfaMailDeliveryState.NotConfigured) throw new InvalidOperationException("MFA recovery delivery is not configured.");
        }
        else
        {
            var operation = challenge.Operation ?? (challenge.Purpose == MfaChallengePurpose.Enrollment ? MfaOperation.EnableTotp : MfaOperation.RebindTotp);
            _ = await delivery.SendSecurityNotificationAsync(challenge.UserId, operation, ct);
        }
        challenge.MailState = MfaMailState.Sent; challenge.RecoveryGrantCiphertext = null;
        await db.SaveChangesAsync(ct);
    }
}

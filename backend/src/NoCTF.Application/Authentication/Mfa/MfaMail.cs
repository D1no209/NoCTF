using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Application.Authentication.Mfa;

public enum MfaMailDeliveryState : short { Sent, NotConfigured, RecipientNotFound }
public interface IMfaEmailDelivery
{
    Task<MfaMailDeliveryState> SendRecoveryAsync(Guid userId, string token, CancellationToken ct);
    Task<MfaMailDeliveryState> SendSecurityNotificationAsync(Guid userId, MfaOperation operation, CancellationToken ct);
}

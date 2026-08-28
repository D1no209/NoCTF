using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;

namespace NoCTF.Worker;

public sealed class AccountNotificationMessageHandler(
    IEmailVerificationDelivery verificationDelivery,
    IPasswordResetEmailDelivery passwordResetDelivery)
{
    public async Task Handle(
        SendEmailVerification message,
        CancellationToken cancellationToken)
    {
        var state = await verificationDelivery.SendVerificationAsync(
            message.UserId,
            message.Token,
            cancellationToken);
        if (state == EmailVerificationDeliveryState.NotConfigured)
        {
            throw new InvalidOperationException(
                "Email verification delivery was queued without a complete SMTP configuration.");
        }
    }

    public async Task Handle(
        SendPasswordReset message,
        CancellationToken cancellationToken)
    {
        var state = await passwordResetDelivery.SendResetAsync(
            message.UserId,
            message.Token,
            cancellationToken);
        if (state == PasswordResetEmailDeliveryState.NotConfigured)
        {
            throw new InvalidOperationException(
                "Password reset delivery was queued without a complete SMTP configuration.");
        }
    }

    public async Task Handle(
        SendPasswordChangedNotification message,
        CancellationToken cancellationToken) =>
        _ = await passwordResetDelivery.SendChangedNotificationAsync(
            message.UserId,
            cancellationToken);
}

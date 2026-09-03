using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using Microsoft.Extensions.Logging;

namespace NoCTF.Worker;

public sealed class AccountNotificationMessageHandler(
    IEmailVerificationDelivery verificationDelivery,
    IPasswordResetEmailDelivery passwordResetDelivery,
    ILogger<AccountNotificationMessageHandler> logger)
{
    public async Task Handle(
        SendEmailVerification message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Consuming {NotificationType} for user {UserId}.",
            nameof(SendEmailVerification),
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "email_verification",
            "attempted");
        EmailVerificationDeliveryState state;
        try
        {
            state = await verificationDelivery.SendVerificationAsync(
                message.UserId,
                message.Token,
                cancellationToken);
        }
        catch (EmailVerificationDeliveryException exception)
        {
            logger.LogWarning(
                "{NotificationType} delivery failed with {Failure} for user {UserId}.",
                nameof(SendEmailVerification),
                exception.Failure,
                message.UserId);
            NoCtfTelemetry.RecordAccountNotificationDelivery(
                "email_verification",
                $"failed_{exception.Failure}");
            throw;
        }
        if (state == EmailVerificationDeliveryState.NotConfigured)
        {
            NoCtfTelemetry.RecordAccountNotificationDelivery(
                "email_verification",
                state.ToString());
            throw new InvalidOperationException(
                "Email verification delivery was queued without a complete SMTP configuration.");
        }
        logger.LogInformation(
            "Completed {NotificationType} with {Outcome} for user {UserId}.",
            nameof(SendEmailVerification),
            state,
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "email_verification",
            state.ToString());
    }

    public async Task Handle(
        SendPasswordReset message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Consuming {NotificationType} for user {UserId}.",
            nameof(SendPasswordReset),
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "password_reset",
            "attempted");
        PasswordResetEmailDeliveryState state;
        try
        {
            state = await passwordResetDelivery.SendResetAsync(
                message.UserId,
                message.Token,
                cancellationToken);
        }
        catch (EmailVerificationDeliveryException exception)
        {
            logger.LogWarning(
                "{NotificationType} delivery failed with {Failure} for user {UserId}.",
                nameof(SendPasswordReset),
                exception.Failure,
                message.UserId);
            NoCtfTelemetry.RecordAccountNotificationDelivery(
                "password_reset",
                $"failed_{exception.Failure}");
            throw;
        }
        if (state == PasswordResetEmailDeliveryState.NotConfigured)
        {
            NoCtfTelemetry.RecordAccountNotificationDelivery(
                "password_reset",
                state.ToString());
            throw new InvalidOperationException(
                "Password reset delivery was queued without a complete SMTP configuration.");
        }
        logger.LogInformation(
            "Completed {NotificationType} with {Outcome} for user {UserId}.",
            nameof(SendPasswordReset),
            state,
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "password_reset",
            state.ToString());
    }

    public async Task Handle(
        SendPasswordChangedNotification message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Consuming {NotificationType} for user {UserId}.",
            nameof(SendPasswordChangedNotification),
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "password_changed",
            "attempted");
        PasswordResetEmailDeliveryState state;
        try
        {
            state = await passwordResetDelivery.SendChangedNotificationAsync(
                message.UserId,
                cancellationToken);
        }
        catch (EmailVerificationDeliveryException exception)
        {
            logger.LogWarning(
                "{NotificationType} delivery failed with {Failure} for user {UserId}.",
                nameof(SendPasswordChangedNotification),
                exception.Failure,
                message.UserId);
            NoCtfTelemetry.RecordAccountNotificationDelivery(
                "password_changed",
                $"failed_{exception.Failure}");
            throw;
        }
        logger.LogInformation(
            "Completed {NotificationType} with {Outcome} for user {UserId}.",
            nameof(SendPasswordChangedNotification),
            state,
            message.UserId);
        NoCtfTelemetry.RecordAccountNotificationDelivery(
            "password_changed",
            state.ToString());
    }
}

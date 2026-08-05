using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Authentication;

public interface IEmailVerificationSmtpClientFactory
{
    SmtpClient Create();
}

public sealed class EmailVerificationSmtpClientFactory : IEmailVerificationSmtpClientFactory
{
    public SmtpClient Create() => new();
}

public sealed class SmtpEmailVerificationDelivery(
    IEmailVerificationConfigurationStore configurationStore,
    IEmailVerificationDeliveryConfigurationReader deliveryConfiguration,
    IUserAuthenticationStore users,
    IEmailVerificationSmtpClientFactory clientFactory,
    ILogger<SmtpEmailVerificationDelivery> logger)
    : IEmailVerificationDelivery,
        IPasswordResetEmailDelivery
{
    public async Task<EmailVerificationDeliveryState> SendVerificationAsync(
        Guid userId,
        string token,
        CancellationToken ct)
    {
        var publicConfiguration = await configurationStore.GetAsync(ct);
        if (!publicConfiguration.Enabled)
            return EmailVerificationDeliveryState.Disabled;

        var configuration = await deliveryConfiguration.GetDeliveryConfigurationAsync(
            requireEnabled: true,
            ct);
        if (configuration is null)
            return EmailVerificationDeliveryState.NotConfigured;

        var user = await users.GetProfileAsync(userId, ct);
        if (user is null)
            return EmailVerificationDeliveryState.RecipientNotFound;

        var publicBaseUri = new Uri(
            $"{configuration.PublicBaseUrl.TrimEnd('/')}/",
            UriKind.Absolute);
        var verificationUrl = new Uri(
            publicBaseUri,
            $"verify-email?token={Uri.EscapeDataString(token)}").AbsoluteUri;
        using var message = CreateMessage(
            configuration,
            user.Email,
            "Verify your NoCTF email address",
            $"Hello {user.UserName},\n\n"
                + "Verify your email address to unlock NoCTF competition features:\n\n"
                + $"{verificationUrl}\n\n"
                + "If you did not create this account, you can ignore this message.",
            $"Hello {WebUtility.HtmlEncode(user.UserName)},<br><br>"
                + "Verify your email address to unlock NoCTF competition features:<br><br>"
                + $"<a href=\"{WebUtility.HtmlEncode(verificationUrl)}\">Verify email address</a><br><br>"
                + "If you did not create this account, you can ignore this message.");
        await SendAsync(configuration, message, ct);
        logger.LogInformation("Sent an email verification message to user {UserId}.", userId);
        return EmailVerificationDeliveryState.Sent;
    }

    public async Task<EmailVerificationDeliveryState> SendTestAsync(
        Guid userId,
        CancellationToken ct)
    {
        var configuration = await deliveryConfiguration.GetDeliveryConfigurationAsync(
            requireEnabled: false,
            ct);
        if (configuration is null)
            return EmailVerificationDeliveryState.NotConfigured;

        var user = await users.GetProfileAsync(userId, ct);
        if (user is null)
            return EmailVerificationDeliveryState.RecipientNotFound;

        const string body = "The NoCTF SMTP configuration is working. No action is required.";
        using var message = CreateMessage(
            configuration,
            user.Email,
            "NoCTF email delivery test",
            body,
            body);
        await SendAsync(configuration, message, ct);
        logger.LogInformation("Sent an SMTP configuration test to user {UserId}.", userId);
        return EmailVerificationDeliveryState.Sent;
    }

    public async Task<PasswordResetEmailDeliveryState> SendResetAsync(
        Guid userId,
        string token,
        CancellationToken ct)
    {
        var configuration = await deliveryConfiguration.GetDeliveryConfigurationAsync(
            requireEnabled: false,
            ct);
        if (configuration is null)
            return PasswordResetEmailDeliveryState.NotConfigured;

        var user = await users.GetProfileAsync(userId, ct);
        if (user is null)
            return PasswordResetEmailDeliveryState.RecipientNotFound;

        var publicBaseUri = new Uri(
            $"{configuration.PublicBaseUrl.TrimEnd('/')}/",
            UriKind.Absolute);
        var resetUrl = new Uri(
            publicBaseUri,
            $"reset-password?token={Uri.EscapeDataString(token)}").AbsoluteUri;
        using var message = CreateMessage(
            configuration,
            user.Email,
            "Reset your NoCTF password",
            $"Hello {user.UserName},\n\n"
                + "Use this single-use link to reset your NoCTF password:\n\n"
                + $"{resetUrl}\n\n"
                + "If you did not request a password reset, you can ignore this message.",
            $"Hello {WebUtility.HtmlEncode(user.UserName)},<br><br>"
                + "Use this single-use link to reset your NoCTF password:<br><br>"
                + $"<a href=\"{WebUtility.HtmlEncode(resetUrl)}\">Reset password</a><br><br>"
                + "If you did not request a password reset, you can ignore this message.");
        await SendAsync(configuration, message, ct);
        logger.LogInformation("Sent a password reset message to user {UserId}.", userId);
        return PasswordResetEmailDeliveryState.Sent;
    }

    public async Task<PasswordResetEmailDeliveryState> SendChangedNotificationAsync(
        Guid userId,
        CancellationToken ct)
    {
        var configuration = await deliveryConfiguration.GetDeliveryConfigurationAsync(
            requireEnabled: false,
            ct);
        if (configuration is null)
            return PasswordResetEmailDeliveryState.NotConfigured;

        var user = await users.GetProfileAsync(userId, ct);
        if (user is null)
            return PasswordResetEmailDeliveryState.RecipientNotFound;

        const string body = "Your NoCTF password was changed. All existing sessions were signed out. "
            + "If you did not make this change, contact a platform administrator immediately.";
        using var message = CreateMessage(
            configuration,
            user.Email,
            "Your NoCTF password was changed",
            body,
            body);
        await SendAsync(configuration, message, ct);
        logger.LogInformation("Sent a password change notice to user {UserId}.", userId);
        return PasswordResetEmailDeliveryState.Sent;
    }

    private static MimeMessage CreateMessage(
        EmailVerificationDeliveryConfiguration configuration,
        string recipient,
        string subject,
        string textBody,
        string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            configuration.SmtpFromName,
            configuration.SmtpFromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody
        }.ToMessageBody();
        return message;
    }

    private async Task SendAsync(
        EmailVerificationDeliveryConfiguration configuration,
        MimeMessage message,
        CancellationToken ct)
    {
        using var client = clientFactory.Create();
        var timeout = TimeSpan.FromSeconds(configuration.SmtpTimeoutSeconds);
        client.Timeout = checked(configuration.SmtpTimeoutSeconds * 1000);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        var operationToken = timeoutSource.Token;

        try
        {
            await ExecutePhaseAsync(
                token => client.ConnectAsync(
                    configuration.SmtpHost,
                    configuration.SmtpPort,
                    ToSecureSocketOptions(configuration.SmtpSecurityMode),
                    token),
                EmailVerificationDeliveryFailure.ConnectionFailed,
                ct,
                operationToken);

            if (!string.IsNullOrWhiteSpace(configuration.SmtpUserName))
            {
                if (configuration.SmtpPassword is null)
                {
                    throw new EmailVerificationDeliveryException(
                        EmailVerificationDeliveryFailure.AuthenticationFailed);
                }

                await ExecutePhaseAsync(
                    token => client.AuthenticateAsync(
                        configuration.SmtpUserName,
                        configuration.SmtpPassword,
                        token),
                    EmailVerificationDeliveryFailure.AuthenticationFailed,
                    ct,
                    operationToken);
            }

            await ExecutePhaseAsync(
                async token => _ = await client.SendAsync(message, token),
                EmailVerificationDeliveryFailure.MessageRejected,
                ct,
                operationToken);
            await ExecutePhaseAsync(
                token => client.DisconnectAsync(true, token),
                EmailVerificationDeliveryFailure.TransportFailed,
                ct,
                operationToken);
        }
        finally
        {
            if (client.IsConnected)
                await TryDisconnectAsync(client, timeout);
        }
    }

    private static async Task ExecutePhaseAsync(
        Func<CancellationToken, Task> operation,
        EmailVerificationDeliveryFailure failure,
        CancellationToken callerToken,
        CancellationToken operationToken)
    {
        try
        {
            await operation(operationToken);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new EmailVerificationDeliveryException(
                EmailVerificationDeliveryFailure.TimedOut);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw new EmailVerificationDeliveryException(
                EmailVerificationDeliveryFailure.TimedOut);
        }
        catch (AuthenticationException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (SslHandshakeException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (SmtpCommandException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (SmtpProtocolException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (ServiceNotAuthenticatedException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (ServiceNotConnectedException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (IOException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (SocketException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
        catch (NotSupportedException)
        {
            throw new EmailVerificationDeliveryException(failure);
        }
    }

    private static async Task TryDisconnectAsync(SmtpClient client, TimeSpan timeout)
    {
        var cleanupTimeout = timeout < TimeSpan.FromSeconds(5)
            ? timeout
            : TimeSpan.FromSeconds(5);
        using var cleanupSource = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await client.DisconnectAsync(false, cleanupSource.Token);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException
                or TimeoutException
                or IOException
                or SocketException
                or SmtpCommandException
                or SmtpProtocolException
                or ServiceNotConnectedException)
        {
            // Best-effort cleanup must not replace the original delivery outcome.
        }
    }

    private static SecureSocketOptions ToSecureSocketOptions(SmtpSecurityMode mode) =>
        mode switch
        {
            SmtpSecurityMode.None => SecureSocketOptions.None,
            SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
}

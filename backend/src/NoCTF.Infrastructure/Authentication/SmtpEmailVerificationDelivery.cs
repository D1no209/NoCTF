using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;
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
            $"auth/verify-email?token={Uri.EscapeDataString(token)}").AbsoluteUri;
        using var message = CreateMessage(
            configuration,
            user.Email,
            "NoCTF 邮箱验证",
            $"你好，{user.UserName}：\n\n"
                + "你正在验证 NoCTF 账户邮箱。请访问以下链接完成验证：\n\n"
                + $"{verificationUrl}\n\n"
                + "如果这不是你的操作，请忽略本邮件。此邮件由系统自动发送，请勿回复。",
            "<!doctype html><html lang=\"zh-CN\"><body>"
                + $"<p>你好，{WebUtility.HtmlEncode(user.UserName)}：</p>"
                + "<p>你正在验证 NoCTF 账户邮箱。请点击下方链接完成验证：</p>"
                + $"<p><a href=\"{WebUtility.HtmlEncode(verificationUrl)}\">验证 NoCTF 邮箱</a></p>"
                + $"<p style=\"word-break:break-all;color:#64748b\">{WebUtility.HtmlEncode(verificationUrl)}</p>"
                + "<p>如果这不是你的操作，请忽略本邮件。此邮件由系统自动发送，请勿回复。</p>"
                + "</body></html>");
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
        var sender = new MailboxAddress(
            configuration.SmtpFromName,
            configuration.SmtpFromAddress);
        var messageId = MimeUtils.GenerateMessageId(sender.Domain);
        var message = new MimeMessage([
            new Header(HeaderId.MessageId, $"<{messageId}>")
        ]);
        message.From.Add(sender);
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Headers.Add("Auto-Submitted", "auto-generated");
        message.Headers.Add("X-Auto-Response-Suppress", "All");
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
        catch (OperationCanceledException exception) when (!callerToken.IsCancellationRequested)
        {
            throw DeliveryException(
                EmailVerificationDeliveryFailure.TimedOut,
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            throw DeliveryException(
                EmailVerificationDeliveryFailure.TimedOut,
                exception);
        }
        catch (AuthenticationException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (SslHandshakeException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (SmtpCommandException exception)
        {
            throw DeliveryException(failure, exception, exception.StatusCode.ToString());
        }
        catch (SmtpProtocolException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (ServiceNotAuthenticatedException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (ServiceNotConnectedException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (IOException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (SocketException exception)
        {
            throw DeliveryException(failure, exception);
        }
        catch (NotSupportedException exception)
        {
            throw DeliveryException(failure, exception);
        }
    }

    private static EmailVerificationDeliveryException DeliveryException(
        EmailVerificationDeliveryFailure failure,
        Exception exception,
        string? smtpStatusCode = null) =>
        new(
            failure,
            new SanitizedSmtpException(exception.GetType().Name, smtpStatusCode));

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

    private sealed class SanitizedSmtpException(
        string exceptionType,
        string? smtpStatusCode)
        : Exception(smtpStatusCode is null
            ? $"SMTP phase failed with {exceptionType}."
            : $"SMTP phase failed with {exceptionType}; status={smtpStatusCode}.");
}

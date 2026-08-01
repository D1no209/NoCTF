using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SmtpEmailVerificationDelivery(
    IEmailVerificationConfigurationStore configurationStore,
    IEmailVerificationDeliveryConfigurationReader deliveryConfiguration,
    IUserAuthenticationStore users,
    ILogger<SmtpEmailVerificationDelivery> logger)
    : IEmailVerificationDelivery
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

        using var message = CreateMessage(
            configuration,
            user.Email,
            "NoCTF email delivery test",
            "The NoCTF SMTP configuration is working. No action is required.");
        await SendAsync(configuration, message, ct);
        logger.LogInformation("Sent an SMTP configuration test to user {UserId}.", userId);
        return EmailVerificationDeliveryState.Sent;
    }

    private static MailMessage CreateMessage(
        EmailVerificationDeliveryConfiguration configuration,
        string recipient,
        string subject,
        string body) =>
        new()
        {
            From = new MailAddress(configuration.SmtpFromAddress, configuration.SmtpFromName),
            To = { new MailAddress(recipient) },
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

    private static async Task SendAsync(
        EmailVerificationDeliveryConfiguration configuration,
        MailMessage message,
        CancellationToken ct)
    {
        using var client = new SmtpClient(configuration.SmtpHost, configuration.SmtpPort)
        {
            Credentials = new NetworkCredential(
                configuration.SmtpUserName,
                configuration.SmtpPassword),
            EnableSsl = configuration.SmtpEnableSsl,
            Timeout = checked(configuration.SmtpTimeoutSeconds * 1000)
        };
        await client.SendMailAsync(message, ct);
    }
}

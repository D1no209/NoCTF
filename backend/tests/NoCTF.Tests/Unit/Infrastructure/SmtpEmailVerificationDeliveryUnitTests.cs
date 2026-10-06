using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Infrastructure.Authentication;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class SmtpEmailVerificationDeliveryUnitTests
{
    [Test]
    [Arguments("https://noctf.test", "reset-token", "https://noctf.test/auth/password-reset?token=reset-token")]
    [Arguments("https://noctf.test/", "a+b/c?&=", "https://noctf.test/auth/password-reset?token=a%2Bb%2Fc%3F%26%3D")]
    [Arguments("https://noctf.test/platform/", "reset-token", "https://noctf.test/platform/auth/password-reset?token=reset-token")]
    public async Task SendResetAsync_uses_the_existing_reset_page_in_both_email_bodies(
        string publicBaseUrl,
        string token,
        string expectedUrl)
    {
        var userId = Guid.NewGuid();
        var configuration = new EmailVerificationDeliveryConfiguration(
            Enabled: false,
            PublicBaseUrl: publicBaseUrl,
            SmtpHost: "smtp.noctf.test",
            SmtpPort: 587,
            SmtpSecurityMode: NoCTF.Domain.Identity.SmtpSecurityMode.StartTls,
            SmtpUserName: string.Empty,
            SmtpPassword: null,
            SmtpFromAddress: "no-reply@noctf.test",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 15);
        var data = SmtpDeliveryTestData.Create(configuration, userId);
        var client = Substitute.ForPartsOf<SmtpClient>();
        client.Configure().ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        client.Configure().DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        string? textBody = null;
        string? htmlBody = null;
        client.Configure().SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(call =>
            {
                var message = call.Arg<MimeMessage>()!;
                textBody = message.TextBody;
                htmlBody = message.HtmlBody;
                return Task.FromResult("accepted");
            });
        var clients = Substitute.For<IEmailVerificationSmtpClientFactory>();
        clients.Create().Returns(client);
        var delivery = new SmtpEmailVerificationDelivery(data.Factory, data.Secrets, clients,
            NullLogger<SmtpEmailVerificationDelivery>.Instance);

        var state = await delivery.SendResetAsync(userId, token, CancellationToken.None);

        await Assert.That(state).IsEqualTo(PasswordResetEmailDeliveryState.Sent);
        await Assert.That(textBody).Contains(expectedUrl);
        await Assert.That(htmlBody).Contains($"href=\"{expectedUrl}\"");
    }

    [Test]
    public async Task SendTestAsync_disabled_verification_does_not_open_an_smtp_delivery()
    {
        var configuration = new EmailVerificationDeliveryConfiguration(
            Enabled: false,
            PublicBaseUrl: "https://noctf.test",
            SmtpHost: "smtp.noctf.test",
            SmtpPort: 587,
            SmtpSecurityMode: NoCTF.Domain.Identity.SmtpSecurityMode.StartTls,
            SmtpUserName: "mailer",
            SmtpPassword: "secret",
            SmtpFromAddress: "no-reply@noctf.test",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 15);
        var data = SmtpDeliveryTestData.Create(
            configuration,
            Guid.NewGuid(),
            includeUser: false);
        var clients = Substitute.For<IEmailVerificationSmtpClientFactory>();
        var delivery = new SmtpEmailVerificationDelivery(
            data.Factory,
            data.Secrets,
            clients,
            NullLogger<SmtpEmailVerificationDelivery>.Instance);

        var state = await delivery.SendTestAsync(Guid.NewGuid(), CancellationToken.None);

        await Assert.That(state).IsEqualTo(EmailVerificationDeliveryState.Disabled);
        clients.DidNotReceiveWithAnyArgs().Create();
    }
}

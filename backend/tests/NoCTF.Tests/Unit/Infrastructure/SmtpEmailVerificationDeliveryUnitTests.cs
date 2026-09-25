using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class SmtpEmailVerificationDeliveryUnitTests
{
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

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class SmtpEmailVerificationDeliveryUnitTests
{
    [Test]
    public async Task SendTestAsync_disabled_verification_does_not_open_an_smtp_delivery()
    {
        var configuration = Substitute.For<IEmailVerificationConfigurationStore>();
        configuration.GetAsync(Arg.Any<CancellationToken>()).Returns(new EmailVerificationConfigurationView(
            Enabled: false,
            PublicBaseUrl: "https://noctf.test",
            TokenLifetimeMinutes: 30,
            ResendCooldownSeconds: 60,
            PasswordResetTokenLifetimeMinutes: 30,
            PasswordResetCooldownSeconds: 60,
            PasswordResetMaxRequestsPerHour: 5,
            SmtpHost: "smtp.noctf.test",
            SmtpPort: 587,
            SmtpSecurityMode: NoCTF.Domain.Identity.SmtpSecurityMode.StartTls,
            SmtpUserName: "mailer",
            SmtpPasswordConfigured: true,
            SmtpFromAddress: "no-reply@noctf.test",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 15,
            UpdatedAt: DateTimeOffset.UnixEpoch));
        var deliveryConfiguration = Substitute.For<IEmailVerificationDeliveryConfigurationReader>();
        var users = Substitute.For<IUserAuthenticationStore>();
        var clients = Substitute.For<IEmailVerificationSmtpClientFactory>();
        var delivery = new SmtpEmailVerificationDelivery(
            configuration,
            deliveryConfiguration,
            users,
            clients,
            NullLogger<SmtpEmailVerificationDelivery>.Instance);

        var state = await delivery.SendTestAsync(Guid.NewGuid(), CancellationToken.None);

        await Assert.That(state).IsEqualTo(EmailVerificationDeliveryState.Disabled);
        await deliveryConfiguration.DidNotReceiveWithAnyArgs()
            .GetDeliveryConfigurationAsync(default, default);
        clients.DidNotReceiveWithAnyArgs().Create();
    }
}

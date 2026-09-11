using System.Security.Cryptography;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Worker;
using NSubstitute;

namespace NoCTF.Tests.Unit.Worker;

public sealed class AccountNotificationReadinessTests
{
    [Test]
    public async Task Disabled_email_verification_does_not_require_delivery_configuration()
    {
        var settings = Substitute.For<IEmailVerificationConfigurationStore>();
        var delivery = Substitute.For<IEmailVerificationDeliveryConfigurationReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Configuration(enabled: false));
        var dependency = new AccountNotificationReadinessDependency(settings, delivery);

        await dependency.CheckAsync(CancellationToken.None);

        await Assert.That(dependency.Describe()["state"])
            .IsEqualTo(AccountNotificationReadinessState.Disabled.ToString());
        await delivery.DidNotReceiveWithAnyArgs()
            .GetDeliveryConfigurationAsync(default, default);
    }

    [Test]
    public async Task Enabled_email_verification_requires_decryptable_delivery_configuration()
    {
        var settings = Substitute.For<IEmailVerificationConfigurationStore>();
        var delivery = Substitute.For<IEmailVerificationDeliveryConfigurationReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Configuration(enabled: true));
        delivery.GetDeliveryConfigurationAsync(true, Arg.Any<CancellationToken>())
            .Returns((EmailVerificationDeliveryConfiguration?)null);
        var dependency = new AccountNotificationReadinessDependency(settings, delivery);

        await Assert.That(() => dependency.CheckAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(dependency.Describe()["state"])
            .IsEqualTo(AccountNotificationReadinessState.ConfigurationUnavailable.ToString());
    }

    [Test]
    public async Task Secret_decryption_failure_keeps_the_worker_unready()
    {
        var settings = Substitute.For<IEmailVerificationConfigurationStore>();
        var delivery = Substitute.For<IEmailVerificationDeliveryConfigurationReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Configuration(enabled: true));
        delivery.GetDeliveryConfigurationAsync(true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmailVerificationDeliveryConfiguration?>(
                new CryptographicException("Wrong deployment key.")));
        var dependency = new AccountNotificationReadinessDependency(settings, delivery);

        await Assert.That(() => dependency.CheckAsync(CancellationToken.None))
            .Throws<CryptographicException>();
        await Assert.That(dependency.Describe()["state"])
            .IsEqualTo(AccountNotificationReadinessState.ConfigurationUnavailable.ToString());
    }

    [Test]
    public async Task Enabled_email_verification_is_ready_with_delivery_configuration()
    {
        var settings = Substitute.For<IEmailVerificationConfigurationStore>();
        var delivery = Substitute.For<IEmailVerificationDeliveryConfigurationReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Configuration(enabled: true));
        delivery.GetDeliveryConfigurationAsync(true, Arg.Any<CancellationToken>())
            .Returns(new EmailVerificationDeliveryConfiguration(
                true,
                "https://noctf.test",
                "smtp.noctf.test",
                587,
                SmtpSecurityMode.StartTls,
                "mailer",
                "secret",
                "no-reply@noctf.test",
                "NoCTF",
                10));
        var dependency = new AccountNotificationReadinessDependency(settings, delivery);

        await dependency.CheckAsync(CancellationToken.None);

        await Assert.That(dependency.Describe()["state"])
            .IsEqualTo(AccountNotificationReadinessState.Ready.ToString());
    }

    private static EmailVerificationConfigurationView Configuration(bool enabled) =>
        new(
            enabled,
            "https://noctf.test",
            60,
            60,
            30,
            60,
            3,
            "smtp.noctf.test",
            587,
            SmtpSecurityMode.StartTls,
            "mailer",
            true,
            "no-reply@noctf.test",
            "NoCTF",
            10,
            DateTimeOffset.UnixEpoch);
}

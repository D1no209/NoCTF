using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
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
        await using var services = Services(settings, delivery);
        var dependency = new AccountNotificationReadinessDependency(
            services.GetRequiredService<IServiceScopeFactory>());

        await dependency.CheckAsync(CancellationToken.None);

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
        await using var services = Services(settings, delivery);
        var dependency = new AccountNotificationReadinessDependency(
            services.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(() => dependency.CheckAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
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
        await using var services = Services(settings, delivery);
        var dependency = new AccountNotificationReadinessDependency(
            services.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(() => dependency.CheckAsync(CancellationToken.None))
            .Throws<CryptographicException>();
    }

    private static ServiceProvider Services(
        IEmailVerificationConfigurationStore settings,
        IEmailVerificationDeliveryConfigurationReader delivery) =>
        new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(delivery)
            .BuildServiceProvider();

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

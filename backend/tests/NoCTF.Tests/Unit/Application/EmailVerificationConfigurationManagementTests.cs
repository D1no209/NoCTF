using NSubstitute;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.Application;

public sealed class EmailVerificationConfigurationManagementTests
{
    [Test]
    public async Task Enabling_requires_a_configured_write_only_smtp_password()
    {
        var store = Substitute.For<IEmailVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Configuration(passwordConfigured: false));
        var manage = new ManageEmailVerificationConfiguration(store);

        var result = await manage.UpdateAsync(Command(enabled: true));

        await Assert.That(result.State).IsEqualTo(EmailVerificationConfigurationUpdateState.Invalid);
        await Assert.That(result.Errors).Contains(EmailVerificationConfigurationError.SmtpPasswordRequired);
        await store.DidNotReceive().UpdateAsync(
            Arg.Any<UpdateEmailVerificationConfigurationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Enabling_without_an_smtp_username_does_not_require_a_password()
    {
        var store = Substitute.For<IEmailVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Configuration(passwordConfigured: false));
        store.UpdateAsync(
                Arg.Any<UpdateEmailVerificationConfigurationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Configuration(passwordConfigured: false));
        var manage = new ManageEmailVerificationConfiguration(store);

        var result = await manage.UpdateAsync(Command(enabled: true) with
        {
            SmtpUserName = string.Empty
        });

        await Assert.That(result.State).IsEqualTo(EmailVerificationConfigurationUpdateState.Updated);
        await store.Received(1).UpdateAsync(
            Arg.Is<UpdateEmailVerificationConfigurationCommand>(command =>
                command != null && command.SmtpUserName == string.Empty),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Replacing_the_password_never_returns_the_secret()
    {
        var store = Substitute.For<IEmailVerificationConfigurationStore>();
        store.ReplacePasswordAsync("replacement-secret", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Configuration(passwordConfigured: true));
        var manage = new ManageEmailVerificationConfiguration(store);

        var result = await manage.ReplacePasswordAsync(
            "replacement-secret",
            DateTimeOffset.UtcNow);

        await Assert.That(result.State).IsEqualTo(EmailVerificationConfigurationUpdateState.Updated);
        await Assert.That(result.Configuration!.SmtpPasswordConfigured).IsTrue();
        await Assert.That(typeof(EmailVerificationConfigurationView).GetProperty("SmtpPassword"))
            .IsNull();
        await store.Received(1).ReplacePasswordAsync(
            "replacement-secret",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static EmailVerificationConfigurationView Configuration(bool passwordConfigured) =>
        new(
            Enabled: false,
            PublicBaseUrl: "https://noctf.example",
            TokenLifetimeMinutes: 1440,
            ResendCooldownSeconds: 60,
            PasswordResetTokenLifetimeMinutes: 30,
            PasswordResetCooldownSeconds: 60,
            PasswordResetMaxRequestsPerHour: 3,
            SmtpHost: "smtp.example",
            SmtpPort: 587,
            SmtpSecurityMode: SmtpSecurityMode.StartTls,
            SmtpUserName: "mailer",
            SmtpPasswordConfigured: passwordConfigured,
            SmtpFromAddress: "no-reply@noctf.example",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 10,
            UpdatedAt: DateTimeOffset.UnixEpoch);

    private static UpdateEmailVerificationConfigurationCommand Command(bool enabled) =>
        new(
            Enabled: enabled,
            PublicBaseUrl: "https://noctf.example",
            TokenLifetimeMinutes: 1440,
            ResendCooldownSeconds: 60,
            PasswordResetTokenLifetimeMinutes: 30,
            PasswordResetCooldownSeconds: 60,
            PasswordResetMaxRequestsPerHour: 3,
            SmtpHost: "smtp.example",
            SmtpPort: 587,
            SmtpSecurityMode: SmtpSecurityMode.StartTls,
            SmtpUserName: "mailer",
            SmtpFromAddress: "no-reply@noctf.example",
            SmtpFromName: "NoCTF",
            SmtpTimeoutSeconds: 10,
            Now: DateTimeOffset.UtcNow);
}

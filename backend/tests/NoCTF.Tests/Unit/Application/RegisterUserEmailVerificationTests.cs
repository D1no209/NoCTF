using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.Application;

public sealed class RegisterUserEmailVerificationTests
{
    [Test]
    [Arguments(EmailVerificationState.Issued, true, true)]
    [Arguments(EmailVerificationState.DeliveryNotConfigured, true, false)]
    [Arguments(EmailVerificationState.Disabled, false, false)]
    public async Task Registration_reports_the_current_verification_delivery_state(
        EmailVerificationState verificationState,
        bool requiresVerification,
        bool emailQueued)
    {
        var users = Substitute.For<IUserRegistrationStore>();
        users.RegisterAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new CreateRegisteredUserResult(
                CreateUserState.Created,
                verificationState));

        var result = await new RegisterUser(users).ExecuteAsync(new(
            "  Player_One  ",
            " player@example.test ",
            "password",
            DateTimeOffset.UtcNow));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Profile.UserName).IsEqualTo("Player_One");
        await Assert.That(result.Value.Profile.EmailVerified).IsEqualTo(!requiresVerification);
        await Assert.That(result.Value.RequiresEmailVerification).IsEqualTo(requiresVerification);
        await Assert.That(result.Value.VerificationEmailQueued).IsEqualTo(emailQueued);
    }
}

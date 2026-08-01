using NoCTF.API.Endpoints.Authentication;

namespace NoCTF.Tests.Unit.API;

public sealed class AuthenticationRequestValidatorTests
{
    [Test]
    [Arguments("player_01")]
    [Arguments("  player_01  ")]
    public async Task Register_accepts_trimmed_username_and_eight_character_password(
        string userName)
    {
        var result = new RegisterValidator().Validate(new RegisterRequest
        {
            UserName = userName,
            Email = "player@example.test",
            Password = "eight888"
        });

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Register_rejects_seven_character_password()
    {
        var result = new RegisterValidator().Validate(new RegisterRequest
        {
            UserName = "player_01",
            Email = "player@example.test",
            Password = "seven77"
        });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Change_password_accepts_eight_character_password()
    {
        var result = new ChangePasswordValidator().Validate(new ChangePasswordRequest
        {
            CurrentPassword = "current-password",
            NewPassword = "eight888"
        });

        await Assert.That(result.IsValid).IsTrue();
    }
}

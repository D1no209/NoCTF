using NoCTF.Bot.Configuration;
using NoCTF.Bot.Providers.Milky;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotOptionsValidatorTests
{
    [Test]
    public async Task NoCtfOptions_RequireHttpsOriginAndToken()
    {
        var validator = new NoCtfBotOptionsValidator();
        var valid = validator.Validate(null, new NoCtfBotOptions
        {
            BaseUrl = new("https://noctf.example.test"),
            PublicBaseUrl = new("https://noctf.example.test"),
            AccessToken = "secret"
        });
        var insecure = validator.Validate(null, new NoCtfBotOptions
        {
            BaseUrl = new("http://noctf.example.test"),
            PublicBaseUrl = new("https://noctf.example.test/path"),
            AccessToken = ""
        });

        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(insecure.Failed).IsTrue();
    }

    [Test]
    public async Task RelayOptions_RequireProviderAndMasterUserId()
    {
        var validator = new RelayOptionsValidator();
        var valid = validator.Validate(null, new RelayOptions
        {
            Provider = "milky",
            MasterUserId = "10001"
        });
        var missing = validator.Validate(null, new RelayOptions
        {
            Provider = "",
            MasterUserId = ""
        });

        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(missing.Failed).IsTrue();
    }

    [Test]
    public async Task MilkyOptions_AllowPrivateHttpButRejectEmbeddedCredentials()
    {
        var validator = new MilkyOptionsValidator();
        var valid = validator.Validate(null, new MilkyOptions
        {
            BaseUrl = new("http://lagrange-milky:8080"),
            AccessToken = "secret"
        });
        var credentialed = validator.Validate(null, new MilkyOptions
        {
            BaseUrl = new("http://user@lagrange-milky:8080"),
            AccessToken = "secret"
        });

        await Assert.That(valid.Succeeded).IsTrue();
        await Assert.That(credentialed.Failed).IsTrue();
    }
}

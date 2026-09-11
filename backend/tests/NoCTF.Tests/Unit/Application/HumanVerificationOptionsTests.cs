using NoCTF.Application.Admission;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;

namespace NoCTF.Tests.Unit.Application;

public sealed class HumanVerificationOptionsTests
{
    [Test]
    public async Task None_does_not_require_provider_credentials()
    {
        var options = new HumanVerificationOptions();

        await Assert.That(options.IsValid(development: false)).IsTrue();
    }

    [Test]
    public async Task Cap_requires_complete_credentials_and_https_in_production()
    {
        var options = Cap("http://cap.example.test/base");

        await Assert.That(options.IsValid(development: true)).IsTrue();
        await Assert.That(options.IsValid(development: false)).IsFalse();

        options.Cap.ServerUrl = "https://cap.example.test/base?unsafe=true";
        await Assert.That(options.IsValid(development: false)).IsFalse();

        options.Cap.ServerUrl = "https://cap.example.test/base";
        await Assert.That(options.IsValid(development: false)).IsTrue();
        await Assert.That(options.CapApiEndpoint().AbsoluteUri)
            .IsEqualTo("https://cap.example.test/base/site-key/");
    }

    [Test]
    public async Task Turnstile_requires_credentials_and_non_local_production_hostnames()
    {
        var options = new HumanVerificationOptions
        {
            Provider = HumanVerificationProvider.Turnstile,
            Turnstile = new TurnstileHumanVerificationOptions
            {
                SiteKey = "site-key",
                Secret = "secret",
                AllowedHostnames = ["localhost"]
            }
        };

        await Assert.That(options.IsValid(development: true)).IsTrue();
        await Assert.That(options.IsValid(development: false)).IsFalse();

        options.Turnstile.AllowedHostnames = ["ctf.example.test"];
        await Assert.That(options.IsValid(development: false)).IsTrue();

        options.Turnstile.AllowedHostnames = [];
        await Assert.That(options.IsValid(development: false)).IsFalse();
    }

    [Test]
    public async Task Api_composition_rejects_an_incomplete_selected_provider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HumanVerification:Provider"] = "Cap",
                ["HumanVerification:Cap:ServerUrl"] = "https://cap.example.test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddNoCtfApi(configuration, includeInfrastructure: false);
        using var provider = services.BuildServiceProvider();

        Func<HumanVerificationOptions> read = () =>
            provider.GetRequiredService<IOptions<HumanVerificationOptions>>().Value;

        await Assert.That(read).Throws<OptionsValidationException>();
    }

    private static HumanVerificationOptions Cap(string serverUrl) => new()
    {
        Provider = HumanVerificationProvider.Cap,
        Cap = new CapHumanVerificationOptions
        {
            ServerUrl = serverUrl,
            SiteKey = "site-key",
            Secret = "secret"
        }
    };
}

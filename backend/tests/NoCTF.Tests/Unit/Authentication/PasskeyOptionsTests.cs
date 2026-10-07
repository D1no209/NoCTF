using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class PasskeyOptionsTests
{
    [Test]
    public async Task Configured_exact_origins_do_not_trust_arbitrary_subdomains()
    {
        var options = new PasskeyOptions { ServerDomain = "noctf.test", AllowedOrigins = ["https://noctf.test"] };
        await Assert.That(options.IsValid(false)).IsTrue();
        await Assert.That(options.AllowsOrigin("https://noctf.test")).IsTrue();
        await Assert.That(options.AllowsOrigin("https://child.noctf.test")).IsFalse();
        await Assert.That(options.AllowsOrigin("https://noctf.test:444")).IsFalse();
    }
    [Test]
    [Arguments("http://noctf.test", false)]
    [Arguments("https://evil.test", false)]
    [Arguments("https://noctf.test/path", false)]
    [Arguments("https://user@noctf.test", false)]
    public async Task Invalid_origins_fail_configuration_validation(string origin, bool expected)
    {
        var options = new PasskeyOptions { ServerDomain = "noctf.test", AllowedOrigins = [origin] };
        await Assert.That(options.IsValid(false)).IsEqualTo(expected);
    }
    [Test]
    public async Task Missing_configuration_disables_passkeys_and_local_HTTP_is_development_only()
    {
        await Assert.That(new PasskeyOptions().IsConfigured).IsFalse();
        var options = new PasskeyOptions { ServerDomain = "localhost", AllowedOrigins = ["http://localhost:3001"] };
        await Assert.That(options.IsValid(true)).IsTrue(); await Assert.That(options.IsValid(false)).IsFalse();
    }
}

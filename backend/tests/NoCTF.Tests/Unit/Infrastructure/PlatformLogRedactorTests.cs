using NoCTF.Infrastructure.Observability;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class PlatformLogRedactorTests
{
    [Test]
    public async Task Secrets_are_redacted_while_flags_remain_visible_to_administrators()
    {
        const string password = "password-value";
        const string shortPassword = "short-password-value";
        const string accessToken = "token-value";
        const string smtpUserName = "mailer@example.test";
        const string flag = "flag{administrator-visible}";
        var properties = new Dictionary<string, object?>
        {
            ["Password"] = password,
            ["pwd"] = shortPassword,
            ["AccessToken"] = accessToken,
            ["SmtpUserName"] = smtpUserName,
            ["Flag"] = flag
        };
        var redacted = PlatformLogRedactor.Redact(
            $"password={password} pwd={shortPassword} token={accessToken} "
                + $"smtpUsername={smtpUserName} flag={flag}",
            properties);

        await Assert.That(redacted).DoesNotContain(password);
        await Assert.That(redacted).DoesNotContain(shortPassword);
        await Assert.That(redacted).DoesNotContain(accessToken);
        await Assert.That(redacted).DoesNotContain(smtpUserName);
        await Assert.That(redacted).Contains(flag);
        await Assert.That(redacted).Contains("[REDACTED]");
    }

    [Test]
    public async Task Bearer_jwt_uri_credentials_and_unstructured_assignments_are_redacted()
    {
        const string jwt =
            "eyJabcdefghijk.eyJabcdefghijklmnop.abcdefghijklmnopqrstu";
        var redacted = PlatformLogRedactor.Redact(
            $"Bearer {jwt} smtps://mailer:password@mail.example.test secret='value' cookie=session "
                + "{\"access_token\":\"json-token\"}",
            []);

        await Assert.That(redacted).DoesNotContain(jwt);
        await Assert.That(redacted).DoesNotContain("mailer:password");
        await Assert.That(redacted).DoesNotContain("'value'");
        await Assert.That(redacted).DoesNotContain("session");
        await Assert.That(redacted).DoesNotContain("json-token");
    }
}

using NoCTF.Infrastructure.Observability;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class PlatformLogRedactorTests
{
    [Test]
    public async Task Mfa_codes_and_provisioning_material_are_redacted()
    {
        const string code = "123456";
        const string recovery = "12345678-ABCDEF00-12345678-ABCDEF00";
        var result = PlatformLogRedactor.Redact($"code={code} recoveryCode={recovery} otpauth://totp/NoCTF:user?secret=EXAMPLEKEY&issuer=NoCTF", []);
        await Assert.That(result).DoesNotContain(code);
        await Assert.That(result).DoesNotContain(recovery);
        await Assert.That(result).DoesNotContain("EXAMPLEKEY");
    }

    [Test]
    public async Task Secrets_and_flags_are_redacted_before_export()
    {
        const string password = "password-value";
        const string shortPassword = "short-password-value";
        const string accessToken = "token-value";
        const string smtpUserName = "mailer@example.test";
        const string userId = "01a0aa11-bb22-7333-8444-cc55dd66ee77";
        const string flag = "flag{administrator-visible}";
        var properties = new Dictionary<string, object?>
        {
            ["Password"] = password,
            ["pwd"] = shortPassword,
            ["AccessToken"] = accessToken,
            ["SmtpUserName"] = smtpUserName,
            ["UserId"] = userId,
            ["Flag"] = flag
        };
        var redacted = PlatformLogRedactor.Redact(
            $"password={password} pwd={shortPassword} token={accessToken} "
                + $"smtpUsername={smtpUserName} userId={userId} flag={flag}",
            properties);

        await Assert.That(redacted).DoesNotContain(password);
        await Assert.That(redacted).DoesNotContain(shortPassword);
        await Assert.That(redacted).DoesNotContain(accessToken);
        await Assert.That(redacted).DoesNotContain(smtpUserName);
        await Assert.That(redacted).DoesNotContain(userId);
        await Assert.That(redacted).DoesNotContain(flag);
        await Assert.That(redacted).Contains("[REDACTED]");
    }

    [Test]
    public async Task Bearer_jwt_uri_credentials_and_unstructured_assignments_are_redacted()
    {
        const string jwt =
            "eyJabcdefghijk.eyJabcdefghijklmnop.abcdefghijklmnopqrstu";
        const string authorizationCode = "sensitive-authorization-code";
        var redacted = PlatformLogRedactor.Redact(
            $"Bearer {jwt} smtps://mailer:password@mail.example.test secret='value' cookie=session "
                + $"{{\"access_token\":\"json-token\"}} "
                + $"https://identity.example.test/callback?code={authorizationCode}&state=value",
            []);

        await Assert.That(redacted).DoesNotContain(jwt);
        await Assert.That(redacted).DoesNotContain("mailer:password");
        await Assert.That(redacted).DoesNotContain("'value'");
        await Assert.That(redacted).DoesNotContain("session");
        await Assert.That(redacted).DoesNotContain("json-token");
        await Assert.That(redacted).DoesNotContain(authorizationCode);
        await Assert.That(redacted).Contains("https://identity.example.test/callback?[REDACTED]");
    }
}

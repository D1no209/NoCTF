using System.Text.Json;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class EmailVerificationConfigurationProtocolTests
{
    [Test]
    public async Task Smtp_security_mode_uses_named_values_and_rejects_integers()
    {
        var request = new PlatformEmailVerificationPatchRequest
        {
            Enabled = true,
            PublicBaseUrl = "https://noctf.test",
            TokenLifetimeMinutes = 1440,
            ResendCooldownSeconds = 60,
            PasswordResetTokenLifetimeMinutes = 30,
            PasswordResetCooldownSeconds = 60,
            PasswordResetMaxRequestsPerHour = 3,
            SmtpHost = "smtp.noctf.test",
            SmtpPort = 587,
            SmtpSecurityMode = SmtpSecurityModeProtocol.StartTls,
            SmtpUserName = string.Empty,
            SmtpFromAddress = "no-reply@noctf.test",
            SmtpFromName = "NoCTF",
            SmtpTimeoutSeconds = 10
        };

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(request, serializerOptions);
        var roundTrip = JsonSerializer.Deserialize<PlatformEmailVerificationPatchRequest>(
            json,
            serializerOptions);

        await Assert.That(json).Contains("\"smtpSecurityMode\":\"StartTls\"");
        await Assert.That(json).DoesNotContain("smtpEnableSsl");
        await Assert.That(roundTrip!.SmtpSecurityMode).IsEqualTo(SmtpSecurityModeProtocol.StartTls);

        var rejected = false;
        try
        {
            _ = JsonSerializer.Deserialize<PlatformEmailVerificationPatchRequest>(
                json.Replace("\"StartTls\"", "2", StringComparison.Ordinal),
                serializerOptions);
        }
        catch (JsonException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }
}

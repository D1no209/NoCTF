using System.Net;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Infrastructure.Competitions.Webhooks;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CompetitionWebhookSenderTests
{
    [Test]
    public async Task Signature_covers_id_timestamp_and_the_exact_body()
    {
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var secret = "whsec_" + Convert.ToBase64String(key);
        var body = Encoding.UTF8.GetBytes("{\"type\":\"com.noctf.webhook.test.v1\",\"data\":{}}");
        const string messageId = "01990000-0000-7000-8000-000000000001";
        const long timestamp = 1_795_000_000;
        var signed = Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.")
            .Concat(body)
            .ToArray();
        var expected = "v1," + Convert.ToBase64String(
            HMACSHA256.HashData(key, signed));

        var actual = CompetitionWebhookSender.Sign(
            messageId,
            timestamp,
            body,
            secret);

        await Assert.That(actual).IsEqualTo(expected);
        await Assert.That(CompetitionWebhookSender.Sign(
            messageId,
            timestamp,
            Encoding.UTF8.GetBytes("{}"),
            secret)).IsNotEqualTo(expected);
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.1")]
    [Arguments("172.16.0.1")]
    [Arguments("192.168.1.1")]
    [Arguments("169.254.169.254")]
    [Arguments("::1")]
    [Arguments("fc00::1")]
    public async Task Private_and_metadata_addresses_are_blocked(string value)
    {
        await Assert.That(CompetitionWebhookSender.IsBlocked(IPAddress.Parse(value)))
            .IsTrue();
    }

    [Test]
    [Arguments("1.1.1.1")]
    [Arguments("8.8.8.8")]
    [Arguments("2606:4700:4700::1111")]
    public async Task Public_addresses_are_allowed(string value)
    {
        await Assert.That(CompetitionWebhookSender.IsBlocked(IPAddress.Parse(value)))
            .IsFalse();
    }
}

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Competitions.Webhooks;
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

    [Test]
    public async Task Successful_delivery_posts_signed_cloudevents_without_redirecting()
    {
        var receiver = StartReceiver(204);

        var result = await receiver.Sender.SendAsync(
            receiver.Delivery,
            CancellationToken.None);
        var request = await receiver.Request;

        await Assert.That(result.Result).IsEqualTo(CompetitionWebhookSendResult.Delivered);
        await Assert.That(result.HttpStatusCode).IsEqualTo(204);
        await Assert.That(request).Contains("POST /hook HTTP/1.1");
        await Assert.That(request).Contains("Content-Type: application/cloudevents+json; charset=utf-8");
        await Assert.That(request).Contains($"webhook-id: {receiver.Delivery.EventId}");
        await Assert.That(request).Contains("webhook-signature: v1,");
        await Assert.That(request).Contains("{\"type\":\"com.noctf.webhook.test.v1\"}");
    }

    [Test]
    public async Task Gone_requests_target_disable_without_retry()
    {
        var receiver = StartReceiver(410);

        var result = await receiver.Sender.SendAsync(
            receiver.Delivery,
            CancellationToken.None);
        _ = await receiver.Request;

        await Assert.That(result.Result).IsEqualTo(CompetitionWebhookSendResult.ReceiverGone);
        await Assert.That(result.HttpStatusCode).IsEqualTo(410);
    }

    [Test]
    [Arguments(408)]
    [Arguments(429)]
    [Arguments(500)]
    [Arguments(503)]
    public async Task Retryable_statuses_raise_transient_failures(int status)
    {
        var receiver = StartReceiver(status);
        Func<Task> action = async () => _ = await receiver.Sender.SendAsync(
            receiver.Delivery,
            CancellationToken.None);

        await Assert.That(action).Throws<CompetitionWebhookTransientException>();
        _ = await receiver.Request;
    }

    [Test]
    public async Task Too_many_requests_preserves_retry_after()
    {
        var receiver = StartReceiver(429, "20");
        CompetitionWebhookTransientException? failure = null;
        try
        {
            _ = await receiver.Sender.SendAsync(receiver.Delivery, CancellationToken.None);
        }
        catch (CompetitionWebhookTransientException exception)
        {
            failure = exception;
        }
        _ = await receiver.Request;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.HttpStatusCode).IsEqualTo(429);
        await Assert.That(failure.RetryAfter is { } retry && retry > DateTimeOffset.UtcNow)
            .IsTrue();
    }

    [Test]
    [Arguments(300)]
    [Arguments(302)]
    [Arguments(400)]
    [Arguments(401)]
    [Arguments(404)]
    public async Task Redirects_and_permanent_client_failures_enter_the_error_path(int status)
    {
        var receiver = StartReceiver(status);
        Func<Task> action = async () => _ = await receiver.Sender.SendAsync(
            receiver.Delivery,
            CancellationToken.None);

        await Assert.That(action).Throws<CompetitionWebhookPermanentException>();
        _ = await receiver.Request;
    }

    [Test]
    [Arguments(200)]
    [Arguments(202)]
    public async Task Staff_receiver_must_acknowledge_durable_delivery_with_204(int status)
    {
        var receiver = StartReceiver(status);
        await Assert.That(async () => await receiver.Sender.SendAsync(receiver.Delivery with { RequireNoContent = true }, CancellationToken.None))
            .Throws<CompetitionWebhookTransientException>();
        _ = await receiver.Request;
    }

    private static ReceiverFixture StartReceiver(int status, string? retryAfter = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = ReceiveOnceAsync(listener, status, retryAfter);
        var options = new CompetitionWebhookOptions(
            new Uri("https://noctf.example.test/"),
            15,
            new HashSet<string>(["127.0.0.1"], StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(["127.0.0.1"], StringComparer.OrdinalIgnoreCase));
        var body = Encoding.UTF8.GetBytes("{\"type\":\"com.noctf.webhook.test.v1\"}");
        var delivery = new CompetitionWebhookDelivery(
            CompetitionWebhookDeliveryReadState.Ready,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new Uri($"http://127.0.0.1:{port}/hook"),
            body,
            "whsec_" + Convert.ToBase64String(Enumerable.Range(1, 32)
                .Select(value => (byte)value).ToArray()));
        return new(new CompetitionWebhookSender(options, TimeProvider.System), delivery, request);
    }

    private static async Task<string> ReceiveOnceAsync(
        TcpListener listener, int status, string? retryAfter)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var received = new MemoryStream();
            var buffer = new byte[4096];
            var headerEnd = -1;
            var contentLength = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;
                received.Write(buffer, 0, read);
                var bytes = received.ToArray();
                if (headerEnd < 0)
                {
                    headerEnd = FindHeaderEnd(bytes);
                    if (headerEnd >= 0)
                    {
                        var headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                        var contentLengthHeader = headers.Split("\r\n")
                            .Single(line => line.StartsWith(
                                "Content-Length:",
                                StringComparison.OrdinalIgnoreCase));
                        contentLength = int.Parse(contentLengthHeader.Split(':', 2)[1].Trim());
                    }
                }
                if (headerEnd >= 0 && received.Length >= headerEnd + 4L + contentLength)
                    break;
            }
            var retryHeader = retryAfter is null ? string.Empty
                : $"Retry-After: {retryAfter}\r\n";
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} Test\r\n{retryHeader}Content-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            return Encoding.UTF8.GetString(received.ToArray());
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> value)
    {
        ReadOnlySpan<byte> marker = "\r\n\r\n"u8;
        return value.IndexOf(marker);
    }

    private sealed record ReceiverFixture(
        CompetitionWebhookSender Sender,
        CompetitionWebhookDelivery Delivery,
        Task<string> Request);
}

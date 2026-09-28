using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Admission;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class HttpHumanVerificationVerifierTests
{
    [Test]
    public async Task Cap_uses_the_official_siteverify_contract()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, """{"success":true}"""));
        using var client = new HttpClient(handler);
        var verifier = CreateVerifier(client);

        var result = await verifier.VerifyAsync(
            Runtime(CapOptions()),
            new("cap-token", HumanVerificationAction.Login, "192.0.2.10"),
            default);

        await Assert.That(result).IsEqualTo(HumanVerificationResult.Verified);
        await Assert.That(handler.RequestUri?.AbsoluteUri)
            .IsEqualTo("https://cap.example.test/root/site-key/siteverify");
        using var body = JsonDocument.Parse(handler.Body!);
        await Assert.That(body.RootElement.GetProperty("secret").GetString()).IsEqualTo("cap-secret");
        await Assert.That(body.RootElement.GetProperty("response").GetString()).IsEqualTo("cap-token");
        await Assert.That(body.RootElement.TryGetProperty("remoteip", out _)).IsFalse();
    }

    [Test]
    public async Task Cap_distinguishes_rejected_tokens_from_provider_failures()
    {
        var rejectedHandler = new RecordingHandler(_ => Json(HttpStatusCode.Forbidden, """{"success":false}"""));
        using var rejectedClient = new HttpClient(rejectedHandler);
        var rejected = await CreateVerifier(rejectedClient).VerifyAsync(
            Runtime(CapOptions()),
            new("bad", HumanVerificationAction.Login, null),
            default);

        var failedHandler = new RecordingHandler(_ => Json(HttpStatusCode.BadGateway, """{"success":false}"""));
        using var failedClient = new HttpClient(failedHandler);
        var unavailable = await CreateVerifier(failedClient).VerifyAsync(
            Runtime(CapOptions()),
            new("bad", HumanVerificationAction.Login, null),
            default);

        await Assert.That(rejected).IsEqualTo(HumanVerificationResult.Rejected);
        await Assert.That(unavailable).IsEqualTo(HumanVerificationResult.Unavailable);
        await Assert.That(failedHandler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Cap_uses_the_backend_endpoint_when_it_is_configured()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"success":true}"""));
        using var client = new HttpClient(handler);
        var options = CapOptions();
        options.Cap.BackendServerUrl = "http://noctf-cap:3000";

        var result = await CreateVerifier(client).VerifyAsync(
            Runtime(options),
            new("cap-token", HumanVerificationAction.Login, null),
            default);

        await Assert.That(result).IsEqualTo(HumanVerificationResult.Verified);
        await Assert.That(handler.RequestUri?.AbsoluteUri)
            .IsEqualTo("http://noctf-cap:3000/site-key/siteverify");
    }

    [Test]
    public async Task Turnstile_sends_remote_ip_and_requires_matching_action_and_hostname()
    {
        var handler = new RecordingHandler(_ => Json(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"ctf.example.test","action":"evaluation"}"""));
        using var client = new HttpClient(handler);
        var verifier = CreateVerifier(client);

        var verified = await verifier.VerifyAsync(
            Runtime(TurnstileOptions()),
            new("turnstile-token", HumanVerificationAction.Evaluation, "198.51.100.3"),
            default);

        await Assert.That(verified).IsEqualTo(HumanVerificationResult.Verified);
        await Assert.That(handler.RequestUri).IsEqualTo(
            new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify"));
        using var body = JsonDocument.Parse(handler.Body!);
        await Assert.That(body.RootElement.GetProperty("secret").GetString()).IsEqualTo("turnstile-secret");
        await Assert.That(body.RootElement.GetProperty("response").GetString()).IsEqualTo("turnstile-token");
        await Assert.That(body.RootElement.GetProperty("remoteip").GetString()).IsEqualTo("198.51.100.3");

        handler.Response = _ => Json(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"other.example.test","action":"evaluation"}""");
        var wrongHost = await verifier.VerifyAsync(
            Runtime(TurnstileOptions()),
            new("new-token", HumanVerificationAction.Evaluation, null),
            default);
        await Assert.That(wrongHost).IsEqualTo(HumanVerificationResult.Rejected);

        handler.Response = _ => Json(
            HttpStatusCode.OK,
            """{"success":true,"hostname":"ctf.example.test","action":"runtime"}""");
        var wrongAction = await verifier.VerifyAsync(
            Runtime(TurnstileOptions()),
            new("another-token", HumanVerificationAction.Evaluation, null),
            default);
        await Assert.That(wrongAction).IsEqualTo(HumanVerificationResult.Rejected);
    }

    [Test]
    public async Task Invalid_provider_json_is_unavailable_and_none_never_calls_http()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        using var client = new HttpClient(handler);
        var invalid = await CreateVerifier(client).VerifyAsync(
            Runtime(TurnstileOptions()),
            new("token", HumanVerificationAction.Runtime, null),
            default);

        var noneOptions = new HumanVerificationOptions();
        var none = await CreateVerifier(client).VerifyAsync(
            Runtime(noneOptions),
            new("", HumanVerificationAction.Runtime, null),
            default);

        await Assert.That(invalid).IsEqualTo(HumanVerificationResult.Unavailable);
        await Assert.That(none).IsEqualTo(HumanVerificationResult.Verified);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    private static HttpHumanVerificationVerifier CreateVerifier(
        HttpClient client) => new(
        new ClientFactory(client),
        NullLogger<HttpHumanVerificationVerifier>.Instance);

    private static HumanVerificationRuntimeConfiguration Runtime(
        HumanVerificationOptions options) => new(true, options);

    private static HumanVerificationOptions CapOptions() => new()
    {
        Provider = HumanVerificationProvider.Cap,
        Cap = new CapHumanVerificationOptions
        {
            ServerUrl = "https://cap.example.test/root",
            SiteKey = "site-key",
            Secret = "cap-secret"
        }
    };

    private static HumanVerificationOptions TurnstileOptions() => new()
    {
        Provider = HumanVerificationProvider.Turnstile,
        Turnstile = new TurnstileHumanVerificationOptions
        {
            SiteKey = "site-key",
            Secret = "turnstile-secret",
            AllowedHostnames = ["ctf.example.test"]
        }
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } = response;
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response(request);
        }
    }
}

using System.Net;
using System.Text;
using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Admission;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CapTelemetryReaderTests
{
    [Test]
    public async Task Reads_daily_aggregates_through_internal_management_api()
    {
        Uri? requested = null;
        string? authorization = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requested = request.RequestUri;
            authorization = request.Headers.GetValues("Authorization").Single();
            return Json(HttpStatusCode.OK,
                """{"stats":{"verified":12,"failed":3,"rateLimited":2,"avgLatency":1450},"key":{"siteKey":"ignored"}}""");
        }));
        var reader = new CapTelemetryReader(new ClientFactory(client));

        var result = await reader.ReadTodayAsync(Options(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(CapTelemetryReadOutcome.Available);
        await Assert.That(result.Snapshot).IsEqualTo(new CapDailyTelemetry(12, 3, 2, 1.45));
        await Assert.That(requested!.AbsoluteUri)
            .IsEqualTo("http://noctf-cap:3000/server/keys/public-key?chartDuration=today");
        await Assert.That(authorization).IsEqualTo("Bot private-management-key");
    }

    [Test]
    public async Task Missing_credential_does_not_call_cap()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        using var client = new HttpClient(handler);
        var options = Options();
        options.ManagementApiKey = string.Empty;

        var result = await new CapTelemetryReader(new ClientFactory(client))
            .ReadTodayAsync(options, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(CapTelemetryReadOutcome.Unconfigured);
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(HttpStatusCode.Unauthorized, CapTelemetryReadOutcome.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden, CapTelemetryReadOutcome.Unauthorized)]
    [Arguments(HttpStatusCode.BadGateway, CapTelemetryReadOutcome.Unavailable)]
    public async Task Provider_errors_are_reported_without_metrics(
        HttpStatusCode status, CapTelemetryReadOutcome expected)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(status, "{}")));

        var result = await new CapTelemetryReader(new ClientFactory(client))
            .ReadTodayAsync(Options(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(expected);
        await Assert.That(result.Snapshot).IsNull();
    }

    [Test]
    [Arguments("not-json")]
    [Arguments("{\"stats\":{}}")]
    [Arguments("{\"stats\":{\"verified\":-1,\"failed\":0,\"rateLimited\":0,\"avgLatency\":0}}")]
    public async Task Invalid_provider_payload_is_unavailable(string body)
    {
        using var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, body)));

        var result = await new CapTelemetryReader(new ClientFactory(client))
            .ReadTodayAsync(Options(), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(CapTelemetryReadOutcome.Unavailable);
    }

    private static CapHumanVerificationOptions Options() => new()
    {
        ServerUrl = "https://noctf.example.test/cap",
        BackendServerUrl = "http://noctf-cap:3000",
        SiteKey = "public-key",
        ManagementApiKey = "private-management-key"
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}

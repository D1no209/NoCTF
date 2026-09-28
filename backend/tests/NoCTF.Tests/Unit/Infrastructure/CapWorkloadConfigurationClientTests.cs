using System.Net;
using System.Text;
using NoCTF.Application.Admission;
using NoCTF.Infrastructure.Admission;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CapWorkloadConfigurationClientTests
{
    [Test]
    public async Task Reads_workload_over_internal_endpoint_with_bot_credential()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK,
                """{"key":{"config":{"difficulty":4,"challengeCount":80,"saltSize":32}}}""");
        });
        var client = Create(handler);

        var result = await client.GetAsync(Options(), CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Configuration!.ExpectedHashAttempts)
            .IsEqualTo(5_242_880L);
        await Assert.That(captured!.RequestUri!.AbsoluteUri)
            .IsEqualTo("http://noctf-cap:3000/server/keys/site-key");
        await Assert.That(captured.Headers.GetValues("Authorization").Single())
            .IsEqualTo("Bot bot-id_token");
    }

    [Test]
    public async Task Update_is_read_back_and_confirmed()
    {
        var requests = new List<(HttpMethod Method, string? Body)>();
        var handler = new StubHandler(async (request, ct) =>
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);
            requests.Add((request.Method, body));
            return request.Method == HttpMethod.Put
                ? Json(HttpStatusCode.OK, """{"success":true}""")
                : Json(HttpStatusCode.OK,
                    """{"key":{"config":{"difficulty":8,"challengeCount":1,"saltSize":32}}}""");
        });
        var client = Create(handler);

        var result = await client.UpdateAsync(
            Options(),
            8,
            1,
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(requests.Select(request => request.Method))
            .IsEquivalentTo([HttpMethod.Put, HttpMethod.Get]);
        await Assert.That(requests[0].Body)
            .Contains("\"difficulty\":8")
            .And.Contains("\"challengeCount\":1");
    }

    [Test]
    public async Task Missing_or_rejected_management_credential_is_specific()
    {
        var noCredential = Options();
        noCredential.ManagementApiKey = string.Empty;
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = Create(handler);

        var missing = await client.GetAsync(noCredential, CancellationToken.None);
        var rejected = await client.GetAsync(Options(), CancellationToken.None);

        await Assert.That(missing.Error).IsEqualTo(
            CapWorkloadConfigurationError.ManagementCredentialMissing);
        await Assert.That(rejected.Error).IsEqualTo(
            CapWorkloadConfigurationError.ManagementCredentialInvalid);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    private static CapWorkloadConfigurationClient Create(StubHandler handler) =>
        new(new StubClientFactory(new HttpClient(handler)));

    private static CapHumanVerificationOptions Options() => new()
    {
        ServerUrl = "https://noctf.example.test/cap",
        BackendServerUrl = "http://noctf-cap:3000",
        SiteKey = "site-key",
        ManagementApiKey = "bot-id_token"
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> handler) => this.handler = handler;

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return handler(request, cancellationToken);
        }
    }
}

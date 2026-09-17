using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Admission;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CapHumanVerificationMonitorTests
{
    [Test]
    public async Task Expected_token_not_found_response_marks_full_chain_healthy()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.TryAddWithoutValidation(
                    "Access-Control-Allow-Origin", "https://noctf.example.test");
                return response;
            }
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    "/assets/cap_wasm_bg.wasm", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0, 97, 115, 109])
                    {
                        Headers = { ContentType = new("application/wasm") }
                    }
                };
            }
            return Json(
                HttpStatusCode.NotFound,
                "{\"success\":false,\"error\":\"Token not found\"}");
        });
        var monitor = CreateMonitor(Cap(enabled: true), handler);

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Healthy);
        await Assert.That(snapshot.Provider)
            .IsEqualTo(HumanVerificationProvider.Cap);
        await Assert.That(snapshot.Enabled).IsTrue();
        await Assert.That(handler.RequestCount).IsEqualTo(3);
    }

    [Test]
    public async Task Invalid_key_or_secret_is_reported_as_misconfigured()
    {
        var handler = SuccessfulStagesWithSiteverify(Json(
            HttpStatusCode.Forbidden,
            "{\"success\":false,\"error\":\"Invalid site key or secret\"}"));
        var monitor = CreateMonitor(Cap(enabled: false), handler);

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Misconfigured);
        await Assert.That(snapshot.Enabled).IsFalse();
    }

    [Test]
    public async Task Wildcard_cors_is_rejected_for_the_configured_public_origin()
    {
        var handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation(
                "Access-Control-Allow-Origin", "*");
            return response;
        });
        var monitor = CreateMonitor(Cap(enabled: true), handler);

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Misconfigured);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(HttpStatusCode.InternalServerError, "{}")]
    [Arguments(HttpStatusCode.NotFound, "not-json")]
    public async Task Provider_failure_or_invalid_json_is_reported_as_unavailable(
        HttpStatusCode status,
        string body)
    {
        var handler = SuccessfulStagesWithSiteverify(Json(status, body));
        var monitor = CreateMonitor(Cap(enabled: true), handler);

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Unavailable);
    }

    [Test]
    public async Task Other_provider_does_not_send_network_requests()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        var configuration = new HumanVerificationRuntimeConfiguration(
            true,
            new HumanVerificationOptions
            {
                Provider = HumanVerificationProvider.Turnstile
            });
        var monitor = CreateMonitor(configuration, handler);

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.NotApplicable);
        await Assert.That(handler.RequestCount).IsEqualTo(0);
    }

    [Test]
    public async Task Stage_timeout_is_reported_as_unavailable()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var monitor = CreateMonitor(
            Cap(enabled: true),
            handler,
            stageTimeout: TimeSpan.FromMilliseconds(20));

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Unavailable);
    }

    [Test]
    public async Task Internal_probe_base_preserves_the_public_cors_origin()
    {
        var requestedHosts = new List<string>();
        var handler = new StubHandler(request =>
        {
            requestedHosts.Add(request.RequestUri!.Authority);
            if (request.Method == HttpMethod.Options)
            {
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.TryAddWithoutValidation(
                    "Access-Control-Allow-Origin", "https://noctf.example.test");
                return response;
            }
            if (request.RequestUri.AbsolutePath.EndsWith(
                    "/assets/cap_wasm_bg.wasm", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0, 97, 115, 109])
                    {
                        Headers = { ContentType = new("application/wasm") }
                    }
                };
            }
            return Json(
                HttpStatusCode.NotFound,
                "{\"success\":false,\"error\":\"Token not found\"}");
        });
        var monitor = CreateMonitor(
            Cap(enabled: true),
            handler,
            backendServerUrl: "http://noctf-cap:3000");

        var snapshot = await monitor.ProbeOnceAsync(CancellationToken.None);

        await Assert.That(snapshot.State)
            .IsEqualTo(HumanVerificationMonitoringState.Healthy);
        await Assert.That(requestedHosts)
            .IsEquivalentTo(["noctf-cap:3000", "noctf-cap:3000", "noctf-cap:3000"]);
    }

    private static CapHumanVerificationMonitor CreateMonitor(
        HumanVerificationRuntimeConfiguration configuration,
        StubHandler handler,
        TimeSpan? stageTimeout = null,
        string backendServerUrl = "")
    {
        configuration.Options.Cap.BackendServerUrl = backendServerUrl;
        var services = new ServiceCollection();
        services.AddSingleton<IHumanVerificationConfigurationReader>(
            new StubConfigurationReader(configuration));
        var provider = services.BuildServiceProvider();
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubClientFactory(new HttpClient(handler)),
            new CapHumanVerificationMonitoringOptions(
                "https://noctf.example.test",
                TimeSpan.FromMinutes(1),
                stageTimeout ?? TimeSpan.FromSeconds(3)),
            TimeProvider.System,
            NullLogger<CapHumanVerificationMonitor>.Instance);
    }

    private static HumanVerificationRuntimeConfiguration Cap(bool enabled) => new(
        enabled,
        new HumanVerificationOptions
        {
            Provider = HumanVerificationProvider.Cap,
            Cap = new()
            {
                ServerUrl = "https://noctf.example.test/cap",
                SiteKey = "site-key",
                Secret = "secret"
            }
        },
        RuntimeEnabled: false,
        EvaluationEnabled: false);

    private static StubHandler SuccessfulStagesWithSiteverify(
        HttpResponseMessage siteverify) => new(request =>
    {
        if (request.Method == HttpMethod.Options)
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation(
                "Access-Control-Allow-Origin", "https://noctf.example.test");
            return response;
        }
        if (request.RequestUri!.AbsolutePath.EndsWith(
                "/assets/cap_wasm_bg.wasm", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0, 97, 115, 109])
                {
                    Headers = { ContentType = new("application/wasm") }
                }
            };
        }
        return siteverify;
    });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubConfigurationReader(
        HumanVerificationRuntimeConfiguration configuration)
        : IHumanVerificationConfigurationReader
    {
        public Task<HumanVerificationRuntimeConfiguration> GetRuntimeConfigurationAsync(
            CancellationToken cancellationToken) => Task.FromResult(configuration);
    }

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> response;
        private int requestCount;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this((request, _) => Task.FromResult(response(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> response) => this.response = response;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return response(request, cancellationToken);
        }
    }
}

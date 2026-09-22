using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Admission;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class CapHumanVerificationConfigurationProbeTests
{
    [Test]
    public async Task Expected_token_not_found_response_accepts_the_full_configuration()
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
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(Cap(), CancellationToken.None);

        await Assert.That(result)
            .IsEqualTo(CapHumanVerificationConfigurationProbeResult.Succeeded);
        await Assert.That(handler.RequestCount).IsEqualTo(3);
    }

    [Test]
    public async Task Invalid_key_or_secret_is_a_configuration_error()
    {
        var handler = SuccessfulStagesWithSiteverify(Json(
            HttpStatusCode.Forbidden,
            "{\"success\":false,\"error\":\"Invalid site key or secret\"}"));

        var result = await CreateProbe(handler)
            .ProbeAsync(Cap(), CancellationToken.None);

        await Assert.That(result).IsEqualTo(
            CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid);
    }

    [Test]
    public async Task Wildcard_cors_is_rejected()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation(
                "Access-Control-Allow-Origin", "*");
            return response;
        });

        var result = await CreateProbe(handler)
            .ProbeAsync(Cap(), CancellationToken.None);

        await Assert.That(result).IsEqualTo(
            CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid);
        await Assert.That(handler.RequestCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(HttpStatusCode.InternalServerError, "{}")]
    [Arguments(HttpStatusCode.NotFound, "not-json")]
    public async Task Provider_failure_or_invalid_json_is_unavailable(
        HttpStatusCode status,
        string body)
    {
        var handler = SuccessfulStagesWithSiteverify(Json(status, body));

        var result = await CreateProbe(handler)
            .ProbeAsync(Cap(), CancellationToken.None);

        await Assert.That(result).IsEqualTo(
            CapHumanVerificationConfigurationProbeResult.ProviderUnavailable);
    }

    [Test]
    public async Task Stage_timeout_is_unavailable()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var result = await CreateProbe(
                handler,
                stageTimeout: TimeSpan.FromMilliseconds(20))
            .ProbeAsync(Cap(), CancellationToken.None);

        await Assert.That(result).IsEqualTo(
            CapHumanVerificationConfigurationProbeResult.ProviderUnavailable);
    }

    [Test]
    public async Task Internal_service_address_preserves_the_public_cors_origin()
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
        var configuration = Cap();
        configuration.Options.Cap.BackendServerUrl = "http://noctf-cap:3000";

        var result = await CreateProbe(handler)
            .ProbeAsync(configuration, CancellationToken.None);

        await Assert.That(result)
            .IsEqualTo(CapHumanVerificationConfigurationProbeResult.Succeeded);
        await Assert.That(requestedHosts)
            .IsEquivalentTo(["noctf-cap:3000", "noctf-cap:3000", "noctf-cap:3000"]);
    }

    private static CapHumanVerificationConfigurationProbe CreateProbe(
        StubHandler handler,
        TimeSpan? stageTimeout = null) => new(
        new StubClientFactory(new HttpClient(handler)),
        new CapHumanVerificationValidationOptions(
            "https://noctf.example.test",
            stageTimeout ?? TimeSpan.FromSeconds(3)),
        NullLogger<CapHumanVerificationConfigurationProbe>.Instance);

    private static HumanVerificationRuntimeConfiguration Cap() => new(
        true,
        new HumanVerificationOptions
        {
            Provider = HumanVerificationProvider.Cap,
            Cap = new()
            {
                ServerUrl = "https://noctf.example.test/cap",
                SiteKey = "site-key",
                Secret = "secret"
            }
        });

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

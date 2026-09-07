using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Administration.Monitoring;
using NoCTF.Infrastructure.Administration.Monitoring;

namespace NoCTF.Tests.Integration.Observability;

[Category("Integration")]
public sealed class PrometheusMonitoringQueryTests
{
    [Test, Timeout(300_000)]
    public async Task Every_monitoring_query_is_valid_in_real_Prometheus_and_idle_latency_has_no_samples(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new ContainerBuilder("prom/prometheus:v3.14.0@sha256:5ce7540c3c00ef4ab0c9d2c995c6a5b9c421f44b4a115d97a2c7af3b1c21cbb0")
                .WithPortBinding(9090, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9090).ForPath("/-/ready"))).Build();
            await container.StartAsync(ct);
            var handler = new QueryHandler();
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(9090)}/") };
            var reader = new PrometheusPlatformMonitoringReader(new ClientFactory(client), TimeProvider.System,
                new PlatformMonitoringOptions(null), PlatformMonitoringThresholds.Default, NullLogger<PrometheusPlatformMonitoringReader>.Instance);
            var result = await reader.ReadAsync(ct);
            await Assert.That(handler.Errors).IsEmpty();
            await Assert.That(result.PrometheusAvailable).IsTrue();
            await Assert.That(result.ApiP95Seconds.State).IsEqualTo(PlatformMonitoringSampleState.NoSamples);
            await Assert.That(result.RedisP99Seconds.Value).IsNull();
            await Assert.That(result.RunnerMinimumAvailableRatio.State).IsEqualTo(PlatformMonitoringSampleState.NoSamples);
        });
    }
    private sealed class QueryHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public ConcurrentBag<string> Errors { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) Errors.Add(await response.Content.ReadAsStringAsync(ct));
            return response;
        }
    }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Administration.Monitoring;
using NoCTF.Infrastructure.Administration.Monitoring;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class PrometheusPlatformMonitoringReaderTests
{
    [Test]
    [Arguments(0, PlatformMonitoringStatus.NoSamples)]
    [Arguments(2, PlatformMonitoringStatus.InsufficientSamples)]
    [Arguments(200, PlatformMonitoringStatus.Warning)]
    public async Task Samples_labels_windows_and_time_are_aligned(int samples, PlatformMonitoringStatus expected)
    {
        var handler = new FixtureHandler(samples);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://prometheus.example.test/") };
        var reader = new PrometheusPlatformMonitoringReader(new ClientFactory(client), TimeProvider.System,
            new PlatformMonitoringOptions(null), PlatformMonitoringThresholds.Default,
            NullLogger<PrometheusPlatformMonitoringReader>.Instance);
        var result = await new ObservePlatformMonitoring(reader, PlatformMonitoringThresholds.Default).ExecuteAsync();
        var redis = result.Metrics.Single(x => x.Kind == PlatformMonitoringMetricKind.RedisP99Milliseconds);
        await Assert.That(redis.Status).IsEqualTo(expected);
        await Assert.That(redis.SampleCount).IsEqualTo(samples);
        await Assert.That(redis.Value).IsEqualTo(samples == 0 ? null : 100);
        var details = result.LatencyDetails!.Where(x => x.Kind == PlatformMonitoringLatencyKind.RedisOperation).ToArray();
        await Assert.That(details.Select(x => x.Endpoint)).IsEquivalentTo(["runner_heartbeat", "runner_claim"]);
        await Assert.That(details.All(x => x.SampleCount == samples && x.WindowSeconds == 300 && x.Status == expected)).IsTrue();
        await Assert.That(result.LatencyDetails!.Take(4).Select(x => x.Kind))
            .IsEquivalentTo([PlatformMonitoringLatencyKind.Rest, PlatformMonitoringLatencyKind.SignalR, PlatformMonitoringLatencyKind.Upload, PlatformMonitoringLatencyKind.Download]);
        await Assert.That(result.PoolResources!.Count).IsEqualTo(6);
        await Assert.That(result.PoolResources.Single(x => x.Pool == "pool-a" && x.Resource == PlatformMonitoringResource.Memory).Available).IsEqualTo(500);
        await Assert.That(result.PoolResources.All(x => x.OnlineRunners == 2)).IsTrue();
        await Assert.That(handler.Queries.Select(x => x.Time).Distinct().Count()).IsEqualTo(1);
        var latencyQueries = handler.Queries.Where(x => x.Query.Contains("noctf_api_request_duration", StringComparison.Ordinal)
            || x.Query.Contains("noctf_redis_operation_duration", StringComparison.Ordinal)).ToArray();
        await Assert.That(latencyQueries.All(x => x.Query.Contains("[5m]", StringComparison.Ordinal))).IsTrue();
        await Assert.That(handler.Queries.Where(x => x.Query.Contains("noctf_api_requests_total", StringComparison.Ordinal))
            .All(x => x.Query.Contains("request_kind=\"rest\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(handler.Queries.Where(x => x.Query.Contains("_bucket", StringComparison.Ordinal))
            .All(x => !x.Query.Contains("outcome=\"success\"", StringComparison.Ordinal))).IsTrue();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixtureHandler(int samples) : HttpMessageHandler
    {
        public ConcurrentBag<(string Query, string Time)> Queries { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var parameters = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            var query = parameters["query"].ToString();
            Queries.Add((query, parameters["time"].ToString()));
            var groupedHttp = query.Contains("by (request_kind)", StringComparison.Ordinal) || query.Contains("by (le,request_kind)", StringComparison.Ordinal);
            var groupedRedis = query.Contains("by (endpoint)", StringComparison.Ordinal) || query.Contains("by (le,endpoint)", StringComparison.Ordinal);
            var groupedPool = query.StartsWith("sum by (pool", StringComparison.Ordinal);
            var labels = groupedHttp ? new[] { "rest", "signalr", "upload", "download" }.Select(x => new Dictionary<string, string> { ["request_kind"] = x }).ToArray()
                : groupedRedis ? new[] { "runner_heartbeat", "runner_claim" }.Select(x => new Dictionary<string, string> { ["endpoint"] = x }).ToArray()
                : groupedPool ? new[] { "pool-a", "pool-b" }.SelectMany(pool => query.Contains("resource", StringComparison.Ordinal)
                    ? new[] { "memory", "cpu", "pids" }.Select(resource => new Dictionary<string, string> { ["pool"] = pool, ["resource"] = resource })
                    : [new Dictionary<string, string> { ["pool"] = pool }]).ToArray()
                : [new Dictionary<string, string>()];
            double value = query.Contains("histogram_quantile", StringComparison.Ordinal) ? 0.1
                : query.Contains("increase(", StringComparison.Ordinal) ? samples
                : query.Contains("capacity_total", StringComparison.Ordinal) ? 1000
                : query.Contains("capacity_available", StringComparison.Ordinal) ? 500
                : query.Contains("runner_online", StringComparison.Ordinal) ? 2 : 1;
            var rows = labels.Select(label => new { metric = label, value = new object[] { 0, value.ToString(CultureInfo.InvariantCulture) } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "success", data = new { resultType = "vector", result = rows } })
            });
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Admission;

namespace NoCTF.Infrastructure.Admission;

/// <summary>Reads Cap's native daily aggregates through its management API, never Valkey.</summary>
public sealed class CapTelemetryReader(IHttpClientFactory clients) : ICapTelemetryReader
{
    public const string ClientName = "cap-telemetry";

    public async Task<CapTelemetryReadResult> ReadTodayAsync(
        CapHumanVerificationOptions options,
        CancellationToken cancellationToken)
    {
        var serverUrl = string.IsNullOrWhiteSpace(options.BackendServerUrl)
            ? options.ServerUrl : options.BackendServerUrl;
        if (string.IsNullOrWhiteSpace(options.SiteKey)
            || string.IsNullOrWhiteSpace(options.ManagementApiKey)
            || !Uri.TryCreate(serverUrl, UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(root.UserInfo)
            || !string.IsNullOrEmpty(root.Query)
            || !string.IsNullOrEmpty(root.Fragment))
            return new(CapTelemetryReadOutcome.Unconfigured);

        var endpoint = new Uri(new Uri(serverUrl.TrimEnd('/') + "/"),
            $"server/keys/{Uri.EscapeDataString(options.SiteKey)}?chartDuration=today");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation(
            "Authorization", $"Bot {options.ManagementApiKey}");
        try
        {
            using var response = await clients.CreateClient(ClientName).SendAsync(
                request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(CapTelemetryReadOutcome.Unauthorized);
            if (!response.IsSuccessStatusCode)
                return new(CapTelemetryReadOutcome.Unavailable);
            if (response.Content is null)
                return new(CapTelemetryReadOutcome.Unavailable);

            var payload = await response.Content.ReadFromJsonAsync(
                CapTelemetryJsonContext.Default.CapTelemetryResponse,
                cancellationToken);
            var stats = payload?.Stats;
            if (stats is null || stats.Verified < 0 || stats.Failed < 0
                || stats.RateLimited < 0 || !double.IsFinite(stats.AvgLatency)
                || stats.AvgLatency < 0)
                return new(CapTelemetryReadOutcome.Unavailable);

            return new(CapTelemetryReadOutcome.Available,
                new CapDailyTelemetry(stats.Verified, stats.Failed,
                    stats.RateLimited, stats.AvgLatency / 1_000));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CapTelemetryReadOutcome.Unavailable);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException or NotSupportedException)
        {
            return new(CapTelemetryReadOutcome.Unavailable);
        }
    }
}

internal sealed record CapTelemetryResponse(
    [property: JsonPropertyName("stats")] CapTelemetryStats? Stats);

internal sealed class CapTelemetryStats
{
    [JsonPropertyName("verified")] public required long Verified { get; init; }
    [JsonPropertyName("failed")] public required long Failed { get; init; }
    [JsonPropertyName("rateLimited")] public required long RateLimited { get; init; }
    [JsonPropertyName("avgLatency")] public required double AvgLatency { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CapTelemetryResponse))]
internal partial class CapTelemetryJsonContext : JsonSerializerContext;

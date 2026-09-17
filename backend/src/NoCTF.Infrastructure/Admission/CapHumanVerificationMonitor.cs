using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Admission;
using NoCTF.Application.Observability;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Admission;

public sealed record CapHumanVerificationMonitoringOptions(
    string PublicOrigin,
    TimeSpan Interval,
    TimeSpan StageTimeout);

public sealed class CapHumanVerificationMonitor(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory clientFactory,
    CapHumanVerificationMonitoringOptions options,
    TimeProvider timeProvider,
    ILogger<CapHumanVerificationMonitor> logger)
    : BackgroundService, IHumanVerificationMonitoringReader
{
    public const string ClientName = "CapHumanVerificationMonitoring";

    private HumanVerificationMonitoringSnapshot _snapshot =
        HumanVerificationMonitoringSnapshot.NotApplicable();

    public Task<HumanVerificationMonitoringSnapshot> ReadAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(Volatile.Read(ref _snapshot));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeAndPublishAsync(stoppingToken);
            try
            {
                await Task.Delay(options.Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task<HumanVerificationMonitoringSnapshot> ProbeOnceAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider
            .GetRequiredService<IHumanVerificationConfigurationReader>();
        var configuration = await reader.GetRuntimeConfigurationAsync(
            cancellationToken);
        var provider = configuration.Options.Provider;
        var checkedAt = timeProvider.GetUtcNow();

        if (provider != HumanVerificationProvider.Cap)
        {
            return new(
                provider,
                configuration.Enabled,
                HumanVerificationMonitoringState.NotApplicable,
                checkedAt,
                null);
        }

        var stopwatch = Stopwatch.StartNew();
        HumanVerificationMonitoringState state;
        try
        {
            state = await ProbeCapAsync(configuration.Options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            state = HumanVerificationMonitoringState.Unavailable;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Cap health probe could not reach the configured service.");
            state = HumanVerificationMonitoringState.Unavailable;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Cap health probe returned an invalid response.");
            state = HumanVerificationMonitoringState.Unavailable;
        }
        stopwatch.Stop();
        return new(
            provider,
            configuration.Enabled,
            state,
            checkedAt,
            Math.Max(0, (long)Math.Ceiling(stopwatch.Elapsed.TotalMilliseconds)));
    }

    private async Task ProbeAndPublishAsync(CancellationToken cancellationToken)
    {
        var previous = Volatile.Read(ref _snapshot);
        var current = await ProbeOnceAsync(cancellationToken);
        Volatile.Write(ref _snapshot, current);
        NoCtfTelemetry.RecordHumanVerificationProbe(
            current.Provider.ToString(),
            current.State.ToString(),
            (current.LatencyMilliseconds ?? 0) / 1000d);
        if (previous.State != current.State || previous.Provider != current.Provider)
        {
            logger.LogInformation(
                "Human verification monitoring changed to {State} for {Provider}.",
                current.State,
                current.Provider);
        }
    }

    private async Task<HumanVerificationMonitoringState> ProbeCapAsync(
        HumanVerificationOptions configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.IsValid(development: false)
            || !TryPublicOrigin(options.PublicOrigin, out var publicOrigin))
        {
            return HumanVerificationMonitoringState.Misconfigured;
        }

        var client = clientFactory.CreateClient(ClientName);
        var capApi = configuration.CapApiEndpoint();
        var challengeUri = new Uri(capApi, "challenge");
        using (var request = new HttpRequestMessage(HttpMethod.Options, challengeUri))
        {
            request.Headers.TryAddWithoutValidation("Origin", publicOrigin);
            request.Headers.TryAddWithoutValidation(
                "Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation(
                "Access-Control-Request-Headers", "content-type");
            using var response = await SendAsync(client, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ClassifyStatus(response.StatusCode);
            if (!response.Headers.TryGetValues(
                    "Access-Control-Allow-Origin", out var origins)
                || !origins.Any(origin =>
                    string.Equals(origin, publicOrigin, StringComparison.Ordinal)))
            {
                return HumanVerificationMonitoringState.Misconfigured;
            }
        }

        var capRoot = new Uri(
            configuration.Cap.ServerUrl.TrimEnd('/') + "/",
            UriKind.Absolute);
        using (var request = new HttpRequestMessage(
                   HttpMethod.Get,
                   new Uri(capRoot, "assets/cap_wasm_bg.wasm")))
        using (var response = await SendAsync(client, request, cancellationToken))
        {
            if (!response.IsSuccessStatusCode)
                return ClassifyStatus(response.StatusCode);
            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/wasm",
                    StringComparison.OrdinalIgnoreCase))
            {
                return HumanVerificationMonitoringState.Misconfigured;
            }
        }

        using (var request = new HttpRequestMessage(
                   HttpMethod.Post,
                   new Uri(capApi, "siteverify")))
        {
            request.Content = JsonContent.Create(new CapSiteverifyRequest(
                configuration.Cap.Secret,
                $"{configuration.Cap.SiteKey}:health:probe"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(options.StageTimeout);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            CapSiteverifyResponse? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<CapSiteverifyResponse>(
                    timeout.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Cap site verification probe returned invalid JSON.");
                return HumanVerificationMonitoringState.Unavailable;
            }

            if (response.StatusCode == HttpStatusCode.NotFound
                && string.Equals(
                    result?.Error,
                    "Token not found",
                    StringComparison.Ordinal))
            {
                return HumanVerificationMonitoringState.Healthy;
            }
            if (string.Equals(
                    result?.Error,
                    "Invalid site key or secret",
                    StringComparison.Ordinal))
            {
                return HumanVerificationMonitoringState.Misconfigured;
            }
            return response.IsSuccessStatusCode
                ? HumanVerificationMonitoringState.Misconfigured
                : ClassifyStatus(response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(options.StageTimeout);
        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
    }

    private static HumanVerificationMonitoringState ClassifyStatus(
        HttpStatusCode statusCode) =>
        (int)statusCode >= 500
            ? HumanVerificationMonitoringState.Unavailable
            : HumanVerificationMonitoringState.Misconfigured;

    private static bool TryPublicOrigin(string value, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/")
        {
            return false;
        }
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private sealed record CapSiteverifyRequest(
        [property: JsonPropertyName("secret")] string Secret,
        [property: JsonPropertyName("response")] string Response);

    private sealed record CapSiteverifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error);
}

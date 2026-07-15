using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// HTTP client wrapper for polling KoH agent endpoints.
/// </summary>
public class KohAgentClient(HttpClient httpClient, ILogger<KohAgentClient> logger)
{
    public async Task<KohAgentPollResult> PollStatusAsync(
        string host, int port, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/status");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "KoH agent at {Host}:{Port} returned HTTP {StatusCode}.",
                    host,
                    port,
                    (int)response.StatusCode);
                return KohAgentPollResult.TransientFailure;
            }

            var status = await response.Content.ReadFromJsonAsync<KohStatusResponse>(ct);
            return status?.Success == true
                ? new KohAgentPollResult(true, status.Data?.Identifier)
                : KohAgentPollResult.TransientFailure;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "KoH agent at {Host}:{Port} unreachable.", host, port);
            return KohAgentPollResult.TransientFailure;
        }
    }

    public async Task<KohStatusResponse?> GetStatusAsync(
        string host, int port, string? apiKey, CancellationToken ct = default)
    {
        var result = await PollStatusAsync(host, port, apiKey, ct);
        return result.IsAuthoritative
            ? new KohStatusResponse(true, string.IsNullOrWhiteSpace(result.Identifier)
                ? null
                : new KohStatusData(result.Identifier))
            : null;
    }

    public async Task<bool> HealthCheckAsync(
        string host, int port, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/healthcheck");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}

public record KohStatusResponse(bool Success, KohStatusData? Data);
public record KohStatusData(string Identifier);
public readonly record struct KohAgentPollResult(bool IsAuthoritative, string? Identifier)
{
    public static KohAgentPollResult TransientFailure => new(false, null);
}

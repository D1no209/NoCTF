using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// HTTP client wrapper for polling KoH agent endpoints.
/// </summary>
public class KohAgentClient(HttpClient httpClient, ILogger<KohAgentClient> logger)
{
    public async Task<KohStatusResponse?> GetStatusAsync(
        string host, int port, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/status");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            var response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<KohStatusResponse>(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "KoH agent at {Host}:{Port} unreachable.", host, port);
            return null;
        }
    }

    public async Task<bool> HealthCheckAsync(
        string host, int port, string? apiKey, CancellationToken ct = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://{host}:{port}/healthcheck");
            if (!string.IsNullOrEmpty(apiKey))
                request.Headers.Add("X-Api-Key", apiKey);

            var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public record KohStatusResponse(bool Success, KohStatusData? Data);
public record KohStatusData(string Identifier);

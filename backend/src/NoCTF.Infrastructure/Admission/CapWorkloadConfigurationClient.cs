using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NoCTF.Application.Admission;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace NoCTF.Infrastructure.Admission;

public sealed class CapWorkloadConfigurationClient(IHttpClientFactory clients)
    : ICapWorkloadConfigurationClient
{
    public const string ClientName = "CapWorkloadConfiguration";

    public async Task<CapWorkloadConfigurationResult> GetAsync(
        CapHumanVerificationOptions options,
        CancellationToken ct)
    {
        var validationError = Validate(options);
        if (validationError is not null)
            return new(Error: validationError);

        using var request = CreateRequest(options, HttpMethod.Get);
        using var response = await SendAsync(request, ct);
        if (response is null)
            return new(Error: CapWorkloadConfigurationError.ProviderUnavailable);
        var statusError = StatusError(response.StatusCode);
        if (statusError is not null)
            return new(Error: statusError);

        CapKeyResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<CapKeyResponse>(ct);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or NotSupportedException
            or System.Text.Json.JsonException)
        {
            return new(Error: CapWorkloadConfigurationError.ProviderUnavailable);
        }

        if (payload?.Key?.Config is null)
            return new(Error: IsKeyNotFound(payload?.Error)
                ? CapWorkloadConfigurationError.SiteKeyNotFound
                : CapWorkloadConfigurationError.ProviderUnavailable);

        var config = payload.Key.Config;
        if (config.Difficulty is < CapWorkloadConfigurationRules.MinimumDifficulty
                or > CapWorkloadConfigurationRules.MaximumDifficulty
            || config.ChallengeCount is < CapWorkloadConfigurationRules.MinimumChallengeCount
                or > CapWorkloadConfigurationRules.MaximumChallengeCount
            || config.SaltSize <= 0)
        {
            return new(Error: CapWorkloadConfigurationError.ProviderUnavailable);
        }

        return new(new(
            config.Difficulty,
            config.ChallengeCount,
            config.SaltSize));
    }

    public async Task<CapWorkloadConfigurationResult> UpdateAsync(
        CapHumanVerificationOptions options,
        int difficulty,
        int challengeCount,
        CancellationToken ct)
    {
        var validationError = Validate(options);
        if (validationError is not null)
            return new(Error: validationError);

        using var request = CreateRequest(options, HttpMethod.Put);
        request.Content = JsonContent.Create(new CapWorkloadUpdateRequest(
            difficulty,
            challengeCount));
        using var response = await SendAsync(request, ct);
        if (response is null)
            return new(Error: CapWorkloadConfigurationError.ProviderUnavailable);
        var statusError = StatusError(response.StatusCode);
        if (statusError is not null)
            return new(Error: statusError);

        CapMutationResponse? mutation;
        try
        {
            mutation = await response.Content.ReadFromJsonAsync<CapMutationResponse>(ct);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or NotSupportedException
            or System.Text.Json.JsonException)
        {
            return new(Error: CapWorkloadConfigurationError.ProviderUnavailable);
        }

        if (mutation?.Success != true)
            return new(Error: IsKeyNotFound(mutation?.Error)
                ? CapWorkloadConfigurationError.SiteKeyNotFound
                : CapWorkloadConfigurationError.ProviderUnavailable);

        var confirmed = await GetAsync(options, ct);
        return confirmed.Configuration is
            {
                Difficulty: var confirmedDifficulty,
                ChallengeCount: var confirmedChallengeCount
            }
            && confirmedDifficulty == difficulty
            && confirmedChallengeCount == challengeCount
                ? confirmed
                : new(Error: confirmed.Error
                    ?? CapWorkloadConfigurationError.ConfigurationNotApplied);
    }

    private async Task<HttpResponseMessage?> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        try
        {
            return await clients.CreateClient(ClientName).SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or BrokenCircuitException
            or TimeoutRejectedException)
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(
        CapHumanVerificationOptions options,
        HttpMethod method)
    {
        var serverUrl = string.IsNullOrWhiteSpace(options.BackendServerUrl)
            ? options.ServerUrl
            : options.BackendServerUrl;
        var root = new Uri(serverUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var suffix = method == HttpMethod.Put ? "/config" : string.Empty;
        var endpoint = new Uri(
            root,
            $"server/keys/{Uri.EscapeDataString(options.SiteKey)}{suffix}");
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"Bot {options.ManagementApiKey}");
        return request;
    }

    private static CapWorkloadConfigurationError? Validate(
        CapHumanVerificationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ManagementApiKey))
            return CapWorkloadConfigurationError.ManagementCredentialMissing;
        if (string.IsNullOrWhiteSpace(options.SiteKey))
            return CapWorkloadConfigurationError.SiteKeyNotFound;
        var serverUrl = string.IsNullOrWhiteSpace(options.BackendServerUrl)
            ? options.ServerUrl
            : options.BackendServerUrl;
        return Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
                ? null
                : CapWorkloadConfigurationError.ProviderUnavailable;
    }

    private static CapWorkloadConfigurationError? StatusError(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                CapWorkloadConfigurationError.ManagementCredentialInvalid,
            HttpStatusCode.NotFound => CapWorkloadConfigurationError.SiteKeyNotFound,
            >= HttpStatusCode.BadRequest =>
                CapWorkloadConfigurationError.ProviderUnavailable,
            _ => null
        };

    private static bool IsKeyNotFound(string? error) =>
        error?.Contains("Key not found", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record CapWorkloadUpdateRequest(
        [property: JsonPropertyName("difficulty")] int Difficulty,
        [property: JsonPropertyName("challengeCount")] int ChallengeCount);

    private sealed record CapMutationResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record CapKeyResponse(
        [property: JsonPropertyName("key")] CapKey? Key,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record CapKey(
        [property: JsonPropertyName("config")] CapKeyConfiguration? Config);

    private sealed record CapKeyConfiguration(
        [property: JsonPropertyName("difficulty")] int Difficulty,
        [property: JsonPropertyName("challengeCount")] int ChallengeCount,
        [property: JsonPropertyName("saltSize")] int SaltSize);
}

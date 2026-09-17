using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace NoCTF.Infrastructure.Admission;

public sealed class HttpHumanVerificationVerifier(
    IHttpClientFactory clients,
    ILogger<HttpHumanVerificationVerifier> logger)
    : IHumanVerificationVerifier
{
    public const string ClientName = "human-verification";
    private static readonly Uri TurnstileSiteverifyUri = new(
        "https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async ValueTask<HumanVerificationResult> VerifyAsync(
        HumanVerificationRuntimeConfiguration configuration,
        HumanVerificationAttempt attempt,
        CancellationToken cancellationToken)
    {
        var options = configuration.Options;
        if (!configuration.Enabled
            || options.Provider == HumanVerificationProvider.None)
            return HumanVerificationResult.Verified;

        try
        {
            return options.Provider switch
            {
                HumanVerificationProvider.Cap => await VerifyCapAsync(
                    options,
                    attempt.Token,
                    cancellationToken),
                HumanVerificationProvider.Turnstile => await VerifyTurnstileAsync(
                    options,
                    attempt,
                    cancellationToken),
                _ => HumanVerificationResult.Unavailable
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(options.Provider, attempt.Action, "timeout");
            return HumanVerificationResult.Unavailable;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or JsonException
                                          or TimeoutRejectedException
                                          or BrokenCircuitException)
        {
            LogUnavailable(options.Provider, attempt.Action, exception.GetType().Name);
            return HumanVerificationResult.Unavailable;
        }
    }

    private async Task<HumanVerificationResult> VerifyCapAsync(
        HumanVerificationOptions options,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(ClientName).PostAsJsonAsync(
            new Uri(options.CapBackendApiEndpoint(), "siteverify"),
            new CapSiteverifyRequest(options.Cap.Secret, token),
            cancellationToken);
        if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            return HumanVerificationResult.Unavailable;

        var result = await response.Content.ReadFromJsonAsync<CapSiteverifyResponse>(
            cancellationToken: cancellationToken);
        if (result is null)
            return HumanVerificationResult.Unavailable;
        return result.Success
            ? HumanVerificationResult.Verified
            : HumanVerificationResult.Rejected;
    }

    private async Task<HumanVerificationResult> VerifyTurnstileAsync(
        HumanVerificationOptions options,
        HumanVerificationAttempt attempt,
        CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient(ClientName).PostAsJsonAsync(
            TurnstileSiteverifyUri,
            new TurnstileSiteverifyRequest(
                options.Turnstile.Secret,
                attempt.Token,
                attempt.RemoteIpAddress),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return HumanVerificationResult.Unavailable;

        var result = await response.Content.ReadFromJsonAsync<TurnstileSiteverifyResponse>(
            cancellationToken: cancellationToken);
        if (result is null)
            return HumanVerificationResult.Unavailable;
        if (!result.Success)
            return HumanVerificationResult.Rejected;

        var expectedAction = ToProtocol(attempt.Action);
        var hostnameAccepted = options.Turnstile.AllowedHostnames.Any(hostname =>
            string.Equals(
                hostname.TrimEnd('.'),
                result.Hostname?.TrimEnd('.'),
                StringComparison.OrdinalIgnoreCase));
        return string.Equals(result.Action, expectedAction, StringComparison.Ordinal)
               && hostnameAccepted
            ? HumanVerificationResult.Verified
            : HumanVerificationResult.Rejected;
    }

    private void LogUnavailable(
        HumanVerificationProvider provider,
        HumanVerificationAction action,
        string reason) =>
        logger.LogWarning(
            "Human verification provider {Provider} was unavailable for {Action}: {Reason}.",
            provider,
            action,
            reason);

    private static string ToProtocol(HumanVerificationAction action) => action switch
    {
        HumanVerificationAction.Login => "login",
        HumanVerificationAction.Registration => "registration",
        HumanVerificationAction.Runtime => "runtime",
        HumanVerificationAction.Evaluation => "evaluation",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    private sealed record CapSiteverifyRequest(
        [property: JsonPropertyName("secret")] string Secret,
        [property: JsonPropertyName("response")] string Response);

    private sealed record CapSiteverifyResponse(
        [property: JsonPropertyName("success")] bool Success);

    private sealed record TurnstileSiteverifyRequest(
        [property: JsonPropertyName("secret")] string Secret,
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("remoteip")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RemoteIpAddress);

    private sealed record TurnstileSiteverifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Admission;

namespace NoCTF.Infrastructure.Admission;

public sealed record CapHumanVerificationValidationOptions(
    string PublicOrigin,
    TimeSpan StageTimeout,
    bool Development = false);

public sealed class CapHumanVerificationConfigurationProbe(
    IHttpClientFactory clientFactory,
    CapHumanVerificationValidationOptions options,
    ILogger<CapHumanVerificationConfigurationProbe> logger)
    : ICapHumanVerificationConfigurationProbe
{
    public const string ClientName = "CapHumanVerificationValidation";

    public async Task<CapHumanVerificationConfigurationProbeResult> ProbeAsync(
        HumanVerificationRuntimeConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.Enabled
            || !configuration.Options.IsValid(options.Development)
            || !TryPublicOrigin(options.PublicOrigin, out var publicOrigin))
        {
            return CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid;
        }

        try
        {
            return await ProbeCapAsync(
                configuration.Options,
                publicOrigin,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CapHumanVerificationConfigurationProbeResult.ProviderUnavailable;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception,
                "Cap configuration validation could not reach the configured service.");
            return CapHumanVerificationConfigurationProbeResult.ProviderUnavailable;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Cap configuration validation returned an invalid response.");
            return CapHumanVerificationConfigurationProbeResult.ProviderUnavailable;
        }
    }

    private async Task<CapHumanVerificationConfigurationProbeResult> ProbeCapAsync(
        HumanVerificationOptions configuration,
        string publicOrigin,
        CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient(ClientName);
        var capApi = configuration.CapBackendApiEndpoint();
        var capRoot = configuration.CapBackendServerRoot();
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
                || !origins.Any(origin => string.Equals(
                    origin,
                    publicOrigin,
                    StringComparison.Ordinal)))
            {
                return CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid;
            }
        }

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
                return CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid;
            }
        }

        using (var request = new HttpRequestMessage(
                   HttpMethod.Post,
                   new Uri(capApi, "siteverify")))
        {
            request.Content = JsonContent.Create(new CapSiteverifyRequest(
                configuration.Cap.Secret,
                $"{configuration.Cap.SiteKey}:configuration:probe"));
            using var response = await SendAsync(client, request, cancellationToken);
            CapSiteverifyResponse? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<CapSiteverifyResponse>(
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Cap configuration validation returned invalid JSON.");
                return CapHumanVerificationConfigurationProbeResult.ProviderUnavailable;
            }

            if (response.StatusCode == HttpStatusCode.NotFound
                && string.Equals(result?.Error, "Token not found", StringComparison.Ordinal))
            {
                return CapHumanVerificationConfigurationProbeResult.Succeeded;
            }
            if (string.Equals(
                    result?.Error,
                    "Invalid site key or secret",
                    StringComparison.Ordinal))
            {
                return CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid;
            }
            return response.IsSuccessStatusCode
                ? CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid
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

    private static CapHumanVerificationConfigurationProbeResult ClassifyStatus(
        HttpStatusCode statusCode) =>
        (int)statusCode >= 500 || statusCode == HttpStatusCode.TooManyRequests
            ? CapHumanVerificationConfigurationProbeResult.ProviderUnavailable
            : CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid;

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

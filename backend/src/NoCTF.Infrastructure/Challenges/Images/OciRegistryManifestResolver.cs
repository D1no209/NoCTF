using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NoCTF.Application.Challenges.Images;

namespace NoCTF.Infrastructure.Challenges.Images;

public sealed class OciRegistryManifestResolver(IHttpClientFactory clients)
    : IContainerRegistryManifestResolver
{
    public const string ClientName = "NoCTF.ContainerRegistry";
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private const int MaxAuthenticationResponseBytes = 64 * 1024;
    private const int MaxBearerTokenLength = 8 * 1024;
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(15);
    private const string OciManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    private const string OciIndexMediaType = "application/vnd.oci.image.index.v1+json";
    private const string DockerManifestMediaType =
        "application/vnd.docker.distribution.manifest.v2+json";
    private const string DockerManifestListMediaType =
        "application/vnd.docker.distribution.manifest.list.v2+json";
    private const string ManifestAccept =
        "application/vnd.oci.image.manifest.v1+json, "
        + "application/vnd.oci.image.index.v1+json, "
        + "application/vnd.docker.distribution.manifest.v2+json, "
        + "application/vnd.docker.distribution.manifest.list.v2+json";

    public async Task<RegistryManifestResolution> ResolveAsync(
        string image,
        CancellationToken ct)
    {
        if (!ContainerImageReference.TryParse(image, out var reference))
        {
            return Failure(
                RegistryManifestFailureCode.InvalidImageReference,
                "Container image reference is invalid.");
        }
        if (reference.IsDigest)
            return new(reference.Pinned(reference.Reference));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ResolveTimeout);
        var requestCancellation = timeout.Token;
        try
        {
            using var first = await SendManifestAsync(reference, null, requestCancellation);
            if (first.StatusCode == HttpStatusCode.Unauthorized)
            {
                var challenge = first.Headers.WwwAuthenticate.FirstOrDefault(value =>
                    string.Equals(value.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
                if (challenge is null)
                {
                    return Failure(
                        RegistryManifestFailureCode.AuthenticationRequired,
                        "Registry requires credentials that are not configured for manifest resolution.");
                }
                var token = await AcquireBearerTokenAsync(challenge, requestCancellation);
                if (!token.Succeeded)
                    return token;
                if (!AuthenticationHeaderValue.TryParse(
                        $"Bearer {token.Token}",
                        out var authorization))
                {
                    return Failure(
                        RegistryManifestFailureCode.AuthenticationFailed,
                        "Registry authentication response contained an invalid access token.");
                }
                using var retry = await SendManifestAsync(
                    reference,
                    authorization,
                    requestCancellation);
                return await ReadManifestAsync(
                    reference,
                    retry,
                    authenticated: true,
                    requestCancellation);
            }
            return await ReadManifestAsync(
                reference,
                first,
                authenticated: false,
                requestCancellation);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(
                RegistryManifestFailureCode.RegistryUnavailable,
                "Registry manifest request timed out.");
        }
        catch (HttpRequestException)
        {
            return Failure(
                RegistryManifestFailureCode.RegistryUnavailable,
                "Registry is unavailable.");
        }
        catch (IOException)
        {
            return Failure(
                RegistryManifestFailureCode.RegistryUnavailable,
                "Registry response could not be read.");
        }
        catch (FormatException)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry returned an invalid manifest response.");
        }
    }

    private async Task<HttpResponseMessage> SendManifestAsync(
        ContainerImageReference image,
        AuthenticationHeaderValue? authorization,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildRegistryUri(
                image.RegistryHost,
                $"v2/{image.Repository}/manifests/{Uri.EscapeDataString(image.Reference)}"));
        request.Headers.TryAddWithoutValidation("Accept", ManifestAccept);
        request.Headers.Authorization = authorization;
        return await clients.CreateClient(ClientName).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
    }

    private async Task<BearerTokenResult> AcquireBearerTokenAsync(
        AuthenticationHeaderValue challenge,
        CancellationToken ct)
    {
        var parameters = ParseAuthenticationParameters(challenge.Parameter);
        if (!parameters.TryGetValue("realm", out var realm)
            || !Uri.TryCreate(realm, UriKind.Absolute, out var endpoint)
            || endpoint.UserInfo.Length > 0
            || (endpoint.Scheme != Uri.UriSchemeHttps
                && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
        {
            return TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry returned an invalid Bearer authentication challenge.");
        }
        var query = new List<KeyValuePair<string, string>>();
        if (parameters.TryGetValue("service", out var service))
            query.Add(new("service", service));
        if (parameters.TryGetValue("scope", out var scope))
            query.Add(new("scope", scope));
        var tokenUri = AppendQuery(endpoint, query);
        using var request = new HttpRequestMessage(HttpMethod.Get, tokenUri);
        using var response = await clients.CreateClient(ClientName).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry rejected the manifest authentication request.");
        }
        if (!response.IsSuccessStatusCode)
        {
            return TokenFailure(
                RegistryManifestFailureCode.RegistryUnavailable,
                "Registry authentication service is unavailable.");
        }
        if (response.Content.Headers.ContentLength is > MaxAuthenticationResponseBytes)
        {
            return TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry authentication response exceeds the supported size.");
        }
        var bytes = await ReadLimitedAsync(
            response.Content,
            MaxAuthenticationResponseBytes,
            ct);
        if (bytes is null)
        {
            return TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry authentication response exceeds the supported size.");
        }
        string? token;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            token = root.TryGetProperty("token", out var tokenProperty)
                ? tokenProperty.GetString()
                : root.TryGetProperty("access_token", out var accessTokenProperty)
                    ? accessTokenProperty.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry returned an invalid authentication response.");
        }
        return string.IsNullOrWhiteSpace(token)
            ? TokenFailure(
                RegistryManifestFailureCode.AuthenticationFailed,
                "Registry authentication response did not contain an access token.")
            : token.Length > MaxBearerTokenLength
                ? TokenFailure(
                    RegistryManifestFailureCode.AuthenticationFailed,
                    "Registry authentication response contained an oversized access token.")
                : new(token);
    }

    private static async Task<RegistryManifestResolution> ReadManifestAsync(
        ContainerImageReference image,
        HttpResponseMessage response,
        bool authenticated,
        CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestNotFound,
                "Registry manifest was not found.");
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Failure(
                authenticated
                    ? RegistryManifestFailureCode.AuthenticationFailed
                    : RegistryManifestFailureCode.AuthenticationRequired,
                authenticated
                    ? "Registry rejected the manifest access token."
                    : "Registry requires authentication for this manifest.");
        }
        if (!response.IsSuccessStatusCode)
        {
            return Failure(
                RegistryManifestFailureCode.RegistryUnavailable,
                $"Registry returned HTTP {(int)response.StatusCode} while resolving the manifest.");
        }
        if (response.Content.Headers.ContentLength is > MaxManifestBytes)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry manifest exceeds the supported size.");
        }
        var bytes = await ReadLimitedAsync(response.Content, MaxManifestBytes, ct);
        if (bytes is null)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry manifest exceeds the supported size.");
        }
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!IsValidManifest(
                    document.RootElement,
                    response.Content.Headers.ContentType?.MediaType))
            {
                return Failure(
                    RegistryManifestFailureCode.ManifestInvalid,
                    "Registry returned an invalid OCI or Docker manifest.");
            }
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry returned an invalid OCI or Docker manifest.");
        }

        var computedDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
        var digestValues = response.Headers.TryGetValues("Docker-Content-Digest", out var values)
            ? values.Distinct(StringComparer.Ordinal).ToArray()
            : [];
        if (digestValues.Length > 1)
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry returned conflicting manifest digests.");
        }
        var digest = digestValues.SingleOrDefault() ?? computedDigest;
        if (!ContainerImageReference.TryParse($"placeholder@{digest}", out _))
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry returned a non-SHA256 manifest digest.");
        }
        if (!string.Equals(digest, computedDigest, StringComparison.Ordinal))
        {
            return Failure(
                RegistryManifestFailureCode.ManifestInvalid,
                "Registry manifest digest does not match its content.");
        }
        return new(image.Pinned(digest));
    }

    private static async Task<byte[]?> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken ct)
    {
        await using var source = await content.ReadAsStreamAsync(ct);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0)
                return destination.ToArray();
            if (destination.Length + read > maximumBytes)
                return null;
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static bool IsValidManifest(JsonElement root, string? responseMediaType)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.GetInt32() != 2)
            return false;
        var mediaType = root.TryGetProperty("mediaType", out var mediaTypeProperty)
            && mediaTypeProperty.ValueKind == JsonValueKind.String
                ? mediaTypeProperty.GetString()
                : responseMediaType;
        return mediaType switch
        {
            OciManifestMediaType or DockerManifestMediaType =>
                root.TryGetProperty("config", out var config)
                && IsValidDescriptor(config)
                && root.TryGetProperty("layers", out var layers)
                && layers.ValueKind == JsonValueKind.Array
                && layers.EnumerateArray().All(IsValidDescriptor),
            OciIndexMediaType or DockerManifestListMediaType =>
                root.TryGetProperty("manifests", out var manifests)
                && manifests.ValueKind == JsonValueKind.Array
                && manifests.GetArrayLength() > 0
                && manifests.EnumerateArray().All(IsValidDescriptor),
            _ => false
        };
    }

    private static bool IsValidDescriptor(JsonElement descriptor) =>
        descriptor.ValueKind == JsonValueKind.Object
        && descriptor.TryGetProperty("mediaType", out var mediaType)
        && mediaType.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(mediaType.GetString())
        && descriptor.TryGetProperty("digest", out var digest)
        && digest.ValueKind == JsonValueKind.String
        && digest.GetString() is { } value
        && ChallengeImagePinningPolicy.IsPinnedImage($"placeholder@{value}")
        && descriptor.TryGetProperty("size", out var size)
        && size.TryGetInt64(out var sizeBytes)
        && sizeBytes > 0;

    private static Uri BuildRegistryUri(string registry, string path)
    {
        var baseUri = new Uri($"https://{registry}");
        var scheme = string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(baseUri.Host, out var address) && IPAddress.IsLoopback(address)
                ? Uri.UriSchemeHttp
                : Uri.UriSchemeHttps;
        return new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
            Path = path
        }.Uri;
    }

    private static IReadOnlyDictionary<string, string> ParseAuthenticationParameters(
        string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return result;
        foreach (var part in value.Split(','))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
                continue;
            var name = part[..separator].Trim();
            var parameterValue = part[(separator + 1)..].Trim().Trim('"');
            if (name.Length > 0 && parameterValue.Length > 0)
                result[name] = parameterValue;
        }
        return result;
    }

    private static Uri AppendQuery(
        Uri uri,
        IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        if (parameters.Count == 0)
            return uri;
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var appended = string.Join('&', parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? appended : $"{existing}&{appended}";
        return builder.Uri;
    }

    private static RegistryManifestResolution Failure(
        RegistryManifestFailureCode code,
        string detail) =>
        new(null, code, detail);

    private static BearerTokenResult TokenFailure(
        RegistryManifestFailureCode code,
        string detail) =>
        new(null, code, detail);

    private sealed record BearerTokenResult(
        string? Token,
        RegistryManifestFailureCode? Failure = null,
        string? Detail = null)
    {
        public bool Succeeded => Token is not null;

        public static implicit operator RegistryManifestResolution(BearerTokenResult result) =>
            new(null, result.Failure, result.Detail);
    }
}

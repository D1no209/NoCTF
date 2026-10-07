using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Authentication;

public sealed class OidcSsoProtocolAdapter(ISsoBackchannel backchannel, TimeProvider? timeProvider = null) : ISsoProtocolAdapter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private const int MaximumMetadataBytes = 256 * 1024;
    private const int MaximumJwksBytes = 1024 * 1024;
    private const int MaximumTokenResponseBytes = 128 * 1024;
    private const int MaximumUserInfoBytes = 128 * 1024;
    private static readonly HashSet<string> AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512
    ];

    public SsoProtocol Protocol => SsoProtocol.Oidc;

    public async Task<SsoAuthorizationRequest> CreateAuthorizationAsync(
        SsoProviderRuntimeConfiguration provider,
        string correlationToken,
        CancellationToken ct)
    {
        var oidc = Required(provider);
        if (string.IsNullOrEmpty(oidc.ClientSecret))
            throw new InvalidOperationException("The OIDC client secret is not configured.");
        var metadata = await LoadMetadataAsync(provider, ct);
        var authorizationEndpoint = EndpointUri(metadata.AuthorizationEndpoint, "authorization endpoint");
        backchannel.ValidateUri(Policy(provider), authorizationEndpoint);
        var nonce = RandomToken(32);
        var verifier = RandomToken(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = CallbackUri(provider).AbsoluteUri;
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = OpenIdConnectResponseType.Code,
            ["client_id"] = oidc.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', oidc.Scopes),
            ["state"] = correlationToken,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        };
        if (oidc.MfaTrust is { Enabled: true } trust) parameters["max_age"] = trust.MaxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var authorizationUrl = AppendQuery(authorizationEndpoint, parameters);
        return new(authorizationUrl, nonce, verifier);
    }

    public async Task<OperationResult<SsoExternalIdentity, SsoFailureCode>> AuthenticateAsync(
        SsoProviderRuntimeConfiguration provider,
        SsoFlowRecord flow,
        string credential,
        CancellationToken ct)
    {
        var oidc = Required(provider);
        if (flow.Nonce is null || flow.PkceVerifier is null || string.IsNullOrWhiteSpace(credential))
            return Failure("The OIDC callback is incomplete.");
        try
        {
            var metadata = await LoadMetadataAsync(provider, ct);
            var tokenEndpoint = EndpointUri(metadata.TokenEndpoint, "token endpoint");
            backchannel.ValidateUri(Policy(provider), tokenEndpoint);
            var token = await ExchangeCodeAsync(
                provider, metadata, tokenEndpoint, flow, credential, ct);
            if (string.IsNullOrWhiteSpace(token.IdToken))
                return Failure("The OIDC token response did not contain an ID Token.");
            var keys = await LoadSigningKeysAsync(provider, metadata, ct);
            ClaimsPrincipal principal;
            JwtSecurityToken validated;
            try
            {
                (principal, validated) = ValidateIdToken(oidc, metadata, token.IdToken, keys);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                keys = await LoadSigningKeysAsync(provider, metadata, ct);
                (principal, validated) = ValidateIdToken(oidc, metadata, token.IdToken, keys);
            }
            if (!string.Equals(principal.FindFirstValue("nonce"), flow.Nonce, StringComparison.Ordinal)
                || principal.FindFirst("iat") is null)
                return Failure("The OIDC nonce or issued-at claim is invalid.");
            var subject = principal.FindFirstValue("sub");
            if (!ValidSubject(subject))
                return Failure("The OIDC Subject is invalid.");
            var audiences = validated.Audiences.ToArray();
            if (audiences.Length > 1
                && !string.Equals(principal.FindFirstValue("azp"), oidc.ClientId, StringComparison.Ordinal))
                return Failure("The OIDC authorized party is invalid.");

            var displayName = principal.FindFirstValue(oidc.DisplayNameClaim);
            if (oidc.ReadUserInfo)
            {
                if (string.IsNullOrWhiteSpace(token.AccessToken)
                    || string.IsNullOrWhiteSpace(metadata.UserInfoEndpoint))
                    return Failure("The OIDC UserInfo response is unavailable.");
                var userInfo = await ReadUserInfoAsync(
                    provider, metadata.UserInfoEndpoint, token.AccessToken, ct);
                if (!string.Equals(userInfo.Subject, subject, StringComparison.Ordinal))
                    return Failure("The OIDC UserInfo Subject did not match the ID Token.");
                displayName = userInfo.DisplayName ?? displayName;
            }
            return OperationResult<SsoExternalIdentity, SsoFailureCode>.Success(new(
                provider.ProviderId,
                SsoProtocol.Oidc,
                oidc.Issuer,
                subject!,
                NormalizeDisplayName(displayName),
                oidc.MfaTrust?.Verify(provider.ProviderId, principal.FindFirstValue("acr"), principal.FindAll("amr").Select(claim => claim.Value).ToArray(), ReadAuthenticationTime(principal), clock.GetUtcNow())));
        }
        catch (Exception exception) when (exception is SecurityTokenException
            or JsonException
            or FormatException
            or InvalidOperationException)
        {
            return Failure("OIDC validation failed.");
        }
    }

    private static DateTimeOffset? ReadAuthenticationTime(ClaimsPrincipal principal)
    {
        if (!long.TryParse(principal.FindFirstValue("auth_time"), out var time)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(time); } catch (ArgumentOutOfRangeException) { return null; }
    }

    private async Task<OidcTokenResponse> ExchangeCodeAsync(
        SsoProviderRuntimeConfiguration provider,
        OpenIdConnectConfiguration metadata,
        Uri tokenEndpoint,
        SsoFlowRecord flow,
        string code,
        CancellationToken ct)
    {
        var oidc = Required(provider);
        var methods = metadata.TokenEndpointAuthMethodsSupported;
        var useBasic = methods.Count == 0
            || methods.Contains("client_secret_basic", StringComparer.Ordinal);
        if (!useBasic && !methods.Contains("client_secret_post", StringComparer.Ordinal))
            throw new InvalidOperationException("The OIDC token endpoint has no supported client authentication method.");
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = OpenIdConnectGrantTypes.AuthorizationCode,
            ["code"] = code,
            ["redirect_uri"] = CallbackUri(provider).AbsoluteUri,
            ["code_verifier"] = flow.PkceVerifier!
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        if (useBasic)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{FormEncode(oidc.ClientId)}:{FormEncode(oidc.ClientSecret)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        else
        {
            values["client_id"] = oidc.ClientId;
            values["client_secret"] = oidc.ClientSecret;
        }
        request.Content = new FormUrlEncodedContent(values);
        var response = await backchannel.SendAsync(
            Policy(provider), request, MaximumTokenResponseBytes, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The OIDC authorization code was rejected.");
        using var json = JsonDocument.Parse(response.Content);
        var root = json.RootElement;
        return new(
            OptionalString(root, "id_token"),
            OptionalString(root, "access_token"));
    }

    private async Task<OpenIdConnectConfiguration> LoadMetadataAsync(
        SsoProviderRuntimeConfiguration provider,
        CancellationToken ct)
    {
        var oidc = Required(provider);
        using var request = new HttpRequestMessage(HttpMethod.Get, oidc.DiscoveryUrl);
        var response = await backchannel.SendAsync(
            Policy(provider), request, MaximumMetadataBytes, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("OIDC discovery failed.");
        var metadata = new OpenIdConnectConfiguration(response.Content);
        if (!string.Equals(metadata.Issuer, oidc.Issuer, StringComparison.Ordinal))
            throw new InvalidOperationException("OIDC discovery returned a different Issuer.");
        if (metadata.ResponseTypesSupported.Count > 0
            && !metadata.ResponseTypesSupported.Contains(
                OpenIdConnectResponseType.Code,
                StringComparer.Ordinal))
            throw new InvalidOperationException("The OIDC provider does not support the authorization code flow.");
        if (metadata.CodeChallengeMethodsSupported.Count > 0
            && !metadata.CodeChallengeMethodsSupported.Contains("S256", StringComparer.Ordinal))
            throw new InvalidOperationException("The OIDC provider does not advertise PKCE S256 support.");
        var policy = Policy(provider);
        backchannel.ValidateUri(policy, EndpointUri(metadata.AuthorizationEndpoint, "authorization endpoint"));
        backchannel.ValidateUri(policy, EndpointUri(metadata.TokenEndpoint, "token endpoint"));
        backchannel.ValidateUri(policy, EndpointUri(metadata.JwksUri, "JWKS endpoint"));
        if (oidc.ReadUserInfo && !string.IsNullOrWhiteSpace(metadata.UserInfoEndpoint))
            backchannel.ValidateUri(policy, EndpointUri(metadata.UserInfoEndpoint, "UserInfo endpoint"));
        return metadata;
    }

    private async Task<IReadOnlyCollection<SecurityKey>> LoadSigningKeysAsync(
        SsoProviderRuntimeConfiguration provider,
        OpenIdConnectConfiguration metadata,
        CancellationToken ct)
    {
        var uri = EndpointUri(metadata.JwksUri, "JWKS endpoint");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await backchannel.SendAsync(
            Policy(provider), request, MaximumJwksBytes, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The OIDC signing keys are unavailable.");
        return new JsonWebKeySet(response.Content).GetSigningKeys().ToArray();
    }

    private static (ClaimsPrincipal Principal, JwtSecurityToken Token) ValidateIdToken(
        OidcSsoRuntimeConfiguration oidc,
        OpenIdConnectConfiguration metadata,
        string idToken,
        IEnumerable<SecurityKey> keys)
    {
        var advertised = metadata.IdTokenSigningAlgValuesSupported.Count == 0
            ? [SecurityAlgorithms.RsaSha256]
            : metadata.IdTokenSigningAlgValuesSupported;
        var algorithms = advertised.Where(AllowedAlgorithms.Contains).ToArray();
        if (algorithms.Length == 0)
            throw new SecurityTokenInvalidAlgorithmException("No safe OIDC signing algorithm is available.");
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = oidc.Issuer,
            ValidateAudience = true,
            ValidAudience = oidc.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidAlgorithms = algorithms
        }, out var token);
        return token is JwtSecurityToken jwt
            ? (principal, jwt)
            : throw new SecurityTokenException("The OIDC ID Token format is invalid.");
    }

    private async Task<OidcUserInfo> ReadUserInfoAsync(
        SsoProviderRuntimeConfiguration provider,
        string endpoint,
        string accessToken,
        CancellationToken ct)
    {
        var oidc = Required(provider);
        var uri = EndpointUri(endpoint, "UserInfo endpoint");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await backchannel.SendAsync(
            Policy(provider), request, MaximumUserInfoBytes, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The OIDC UserInfo request failed.");
        using var json = JsonDocument.Parse(response.Content);
        return new(
            OptionalString(json.RootElement, "sub"),
            OptionalString(json.RootElement, oidc.DisplayNameClaim));
    }

    private static OidcSsoRuntimeConfiguration Required(SsoProviderRuntimeConfiguration provider) =>
        provider.Oidc ?? throw new InvalidOperationException("OIDC provider settings are missing.");

    private static SsoBackchannelPolicy Policy(SsoProviderRuntimeConfiguration provider) =>
        new(provider.TimeoutSeconds, provider.AllowedHosts);

    private static Uri CallbackUri(SsoProviderRuntimeConfiguration provider) => new(
        new Uri(provider.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
        $"api/v1/auth/sso/callback/oidc/{provider.ProviderId:D}");

    private static Uri EndpointUri(string? value, string name) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0
            ? uri
            : throw new InvalidOperationException($"The OIDC {name} is invalid.");

    private static Uri AppendQuery(Uri uri, IReadOnlyDictionary<string, string> values)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        var query = string.Join('&', values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        builder.Query = string.IsNullOrEmpty(existing) ? query : $"{existing}&{query}";
        return builder.Uri;
    }

    private static string FormEncode(string value) =>
        Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    private static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ValidSubject(string? value) => value is { Length: >= 1 and <= 255 }
        && value.All(character => character <= 0x7F && !char.IsControl(character));

    private static string? NormalizeDisplayName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized)
            ? null
            : normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static OperationResult<SsoExternalIdentity, SsoFailureCode> Failure(string message) =>
        OperationResult<SsoExternalIdentity, SsoFailureCode>.Failure(
            SsoFailureCode.AuthenticationFailed,
            message);

    private sealed record OidcTokenResponse(string? IdToken, string? AccessToken);
    private sealed record OidcUserInfo(string? Subject, string? DisplayName);
}

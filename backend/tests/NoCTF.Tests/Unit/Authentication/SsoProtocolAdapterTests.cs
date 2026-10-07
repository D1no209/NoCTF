using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class SsoProtocolAdapterTests
{
    [Test]
    public async Task Oidc_code_flow_uses_pkce_and_validates_the_id_token(
        CancellationToken cancellationToken)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        string? token = null;
        string? tokenRequest = null;
        var backchannel = new FakeBackchannel(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("openid-configuration", StringComparison.Ordinal))
                return Response(Metadata());
            if (path == "/jwks")
                return Response(Jwks(rsa, key.KeyId));
            if (path == "/token")
            {
                tokenRequest = request.Content!.ReadAsStringAsync(cancellationToken)
                    .GetAwaiter().GetResult();
                return Response(JsonSerializer.Serialize(new { id_token = token }));
            }
            throw new InvalidOperationException($"Unexpected OIDC request {path}.");
        });
        var adapter = new OidcSsoProtocolAdapter(backchannel);
        var provider = OidcProvider();
        var authorization = await adapter.CreateAuthorizationAsync(
            provider, "state-value", cancellationToken);
        token = IdToken(key, provider.Oidc!.Issuer, provider.Oidc.ClientId,
            authorization.Nonce!, "subject-1");
        var flow = Flow(
            provider,
            SsoProtocol.Oidc,
            authorization.Nonce,
            authorization.PkceVerifier);

        var result = await adapter.AuthenticateAsync(
            provider, flow, "authorization-code", cancellationToken);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.Subject).IsEqualTo("subject-1");
        await Assert.That(authorization.AuthorizationUrl.Query).Contains("code_challenge_method=S256");
        await Assert.That(authorization.AuthorizationUrl.Query).Contains("state=state-value");
        await Assert.That(tokenRequest).Contains("code_verifier=");
        await Assert.That(tokenRequest).Contains("grant_type=authorization_code");
    }

    [Test]
    [Arguments("otp", 0, true, true)]
    [Arguments("otp", 301, true, false)]
    [Arguments("otp", 0, false, false)]
    [Arguments("OTP", 0, true, false)]
    public async Task Oidc_Mfa_uses_only_signed_fresh_authentication_claims(string secondMethod, int ageSeconds, bool includeAuthenticationTime, bool expectedProof, CancellationToken ct)
    {
        using var rsa = RSA.Create(2048); var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        string? token = null;
        var backchannel = new FakeBackchannel(request => request.RequestUri!.AbsolutePath switch {
            "/.well-known/openid-configuration" => Response(Metadata()), "/jwks" => Response(Jwks(rsa, key.KeyId)),
            "/token" => Response(JsonSerializer.Serialize(new { id_token = token })), _ => throw new InvalidOperationException() });
        var original = OidcProvider();
        var provider = original with { Oidc = original.Oidc! with { MfaTrust = new(true, Guid.NewGuid(), 300, [], [["pwd", "otp"]]) } };
        var adapter = new OidcSsoProtocolAdapter(backchannel);
        var authorization = await adapter.CreateAuthorizationAsync(provider, "state", ct);
        var claims = new List<Claim> { new("amr", "pwd"), new("amr", secondMethod), new("amr", "extra") };
        if (includeAuthenticationTime) claims.Add(new("auth_time", DateTimeOffset.UtcNow.AddSeconds(-ageSeconds).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        token = IdToken(key, provider.Oidc.Issuer, provider.Oidc.ClientId, authorization.Nonce!, "subject", claims);
        var result = await adapter.AuthenticateAsync(provider, Flow(provider, SsoProtocol.Oidc, authorization.Nonce, authorization.PkceVerifier), "code", ct);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Value!.MfaProof is not null).IsEqualTo(expectedProof);
        await Assert.That(authorization.AuthorizationUrl.Query).Contains("max_age=300");
    }

    [Test]
    public async Task Oidc_rejects_a_nonce_mismatch(CancellationToken cancellationToken)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var token = IdToken(key, "https://id.example.test", "noctf", "token-nonce", "subject-1");
        var backchannel = new FakeBackchannel(request => request.RequestUri!.AbsolutePath switch
        {
            "/.well-known/openid-configuration" => Response(Metadata()),
            "/jwks" => Response(Jwks(rsa, key.KeyId)),
            "/token" => Response(JsonSerializer.Serialize(new { id_token = token })),
            _ => throw new InvalidOperationException()
        });
        var provider = OidcProvider();
        var result = await new OidcSsoProtocolAdapter(backchannel).AuthenticateAsync(
            provider,
            Flow(provider, SsoProtocol.Oidc, "different-nonce", "verifier-value"),
            "authorization-code",
            cancellationToken);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(SsoFailureCode.AuthenticationFailed);
    }

    private static SsoProviderRuntimeConfiguration OidcProvider() => new(
        GlobalEnabled: true,
        "https://noctf.example",
        Guid.Parse("00000000-0000-0000-0000-000000000001"),
        "Example OIDC",
        SsoProtocol.Oidc,
        Enabled: true,
        AllowLogin: true,
        AllowBinding: true,
        TimeoutSeconds: 10,
        AllowedHosts: ["id.example.test"],
        Fingerprint: "fingerprint",
        Oidc: new(
            "https://id.example.test",
            "https://id.example.test/.well-known/openid-configuration",
            "noctf",
            "client-secret",
            ["openid", "profile"],
            ReadUserInfo: false,
            "name"),
        Cas: null);

    private static SsoFlowRecord Flow(
        SsoProviderRuntimeConfiguration provider,
        SsoProtocol protocol,
        string? nonce = null,
        string? verifier = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new(
            Guid.CreateVersion7(now),
            provider.ProviderId,
            protocol,
            SsoFlowIntent.Login,
            "browser",
            SsoFlowState.Processing,
            "state",
            provider.Fingerprint,
            "/",
            now,
            now.AddMinutes(10),
            Nonce: nonce,
            PkceVerifier: verifier,
            ProcessingToken: "processing");
    }

    private static string Metadata() => """
        {
          "issuer":"https://id.example.test",
          "authorization_endpoint":"https://id.example.test/authorize",
          "token_endpoint":"https://id.example.test/token",
          "jwks_uri":"https://id.example.test/jwks",
          "response_types_supported":["code"],
          "subject_types_supported":["public"],
          "id_token_signing_alg_values_supported":["RS256"],
          "token_endpoint_auth_methods_supported":["client_secret_basic"]
        }
        """;

    private static string IdToken(
        SecurityKey key,
        string issuer,
        string audience,
        string nonce,
        string subject, IReadOnlyList<Claim>? extra = null)
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            new Claim[]
            {
                new Claim("sub", subject),
                new Claim("nonce", nonce),
                new Claim("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("name", "Test User")
            }.Concat(extra ?? []),
            now.AddMinutes(-1).UtcDateTime,
            now.AddMinutes(5).UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Jwks(RSA rsa, string keyId)
    {
        var parameters = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = keyId,
                    alg = "RS256",
                    n = Base64Url(parameters.Modulus!),
                    e = Base64Url(parameters.Exponent!)
                }
            }
        });
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static SsoBackchannelResponse Response(string content) =>
        new(HttpStatusCode.OK, content, null);

    private sealed class FakeBackchannel(
        Func<HttpRequestMessage, SsoBackchannelResponse> responder) : ISsoBackchannel
    {
        public Task<SsoBackchannelResponse> SendAsync(
            SsoBackchannelPolicy policy,
            HttpRequestMessage request,
            int maximumResponseBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));

        public void ValidateUri(SsoBackchannelPolicy policy, Uri uri)
        {
            if (!policy.AllowedHosts.Contains(uri.DnsSafeHost, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Host rejected by test backchannel.");
        }
    }
}

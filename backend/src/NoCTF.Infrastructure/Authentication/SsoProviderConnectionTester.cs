using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SsoProviderConnectionTester(
    NoCtfDbContext db,
    ISsoBackchannel backchannel) : ISsoProviderConnectionTester
{
    public async Task<SsoProviderConnectionTestResult> TestAsync(
        Guid providerId,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.AsNoTracking().AsSplitQuery()
            .SingleAsync(ct);
        var provider = settings.SsoConfiguration.Providers
            .SingleOrDefault(item => item.Id == providerId);
        if (provider is null)
            return new(false, null, null, "configuration", "ProviderNotFound");
        try
        {
            return provider.Protocol switch
            {
                SsoProtocol.Oidc => await TestOidcAsync(provider, ct),
                SsoProtocol.Cas => await TestCasAsync(provider, ct),
                _ => new(false, provider.Protocol, null, "configuration", "ProtocolUnsupported")
            };
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or XmlException
            or InvalidOperationException)
        {
            return new(false, provider.Protocol, provider.Oidc?.Issuer,
                "connection", exception.GetType().Name);
        }
    }

    private async Task<SsoProviderConnectionTestResult> TestOidcAsync(
        SsoProviderConfiguration provider,
        CancellationToken ct)
    {
        var oidc = provider.Oidc
            ?? throw new InvalidOperationException("OIDC settings are missing.");
        using var request = new HttpRequestMessage(HttpMethod.Get, oidc.DiscoveryUrl);
        var response = await backchannel.SendAsync(
            new(provider.TimeoutSeconds, provider.AllowedHosts),
            request,
            256 * 1024,
            ct);
        if (!response.IsSuccessStatusCode)
            return new(false, provider.Protocol, oidc.Issuer, "discovery",
                $"Http{(int)response.StatusCode}");
        using var metadata = JsonDocument.Parse(response.Content);
        var root = metadata.RootElement;
        var issuer = RequiredString(root, "issuer");
        var policy = new SsoBackchannelPolicy(provider.TimeoutSeconds, provider.AllowedHosts);
        backchannel.ValidateUri(policy, RequiredEndpointUri(root, "authorization_endpoint"));
        backchannel.ValidateUri(policy, RequiredEndpointUri(root, "token_endpoint"));
        backchannel.ValidateUri(policy, RequiredEndpointUri(root, "jwks_uri"));
        if (!string.Equals(issuer, oidc.Issuer, StringComparison.Ordinal))
            return new(false, provider.Protocol, issuer, "discovery", "IssuerMismatch");
        return new(true, provider.Protocol, issuer, null, null);
    }

    private async Task<SsoProviderConnectionTestResult> TestCasAsync(
        SsoProviderConfiguration provider,
        CancellationToken ct)
    {
        var cas = provider.Cas
            ?? throw new InvalidOperationException("CAS settings are missing.");
        var uri = new UriBuilder(cas.ServiceValidateUrl)
        {
            Query = "service=https%3A%2F%2Finvalid.example%2F&ticket=ST-NOCTF-CONNECTION-TEST"
        }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await backchannel.SendAsync(
            new(provider.TimeoutSeconds, provider.AllowedHosts),
            request,
            64 * 1024,
            ct);
        if ((int)response.StatusCode >= 500)
            return new(false, provider.Protocol, null, "serviceValidate",
                $"Http{(int)response.StatusCode}");
        using var reader = XmlReader.Create(
            new StringReader(response.Content),
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 64 * 1024
            });
        var document = XDocument.Load(reader, LoadOptions.None);
        XNamespace ns = "http://www.yale.edu/tp/cas";
        if (document.Root?.Name != ns + "serviceResponse"
            || document.Root.Elements().All(element =>
                element.Name != ns + "authenticationFailure"
                && element.Name != ns + "authenticationSuccess"))
            return new(false, provider.Protocol, null, "serviceValidate", "InvalidCasResponse");
        return new(true, provider.Protocol, null, null, null);
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"OIDC metadata property {name} is missing.");

    private static Uri RequiredEndpointUri(JsonElement root, string name)
    {
        var value = RequiredString(root, name);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            ? uri
            : throw new JsonException($"OIDC metadata property {name} is not an HTTP(S) URI.");
    }
}

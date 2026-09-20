using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SsoProviderRuntimeReader(
    NoCtfDbContext db,
    PlatformSecretProtector secrets) : ISsoProviderRuntimeReader
{
    public async Task<SsoProviderRuntimeConfiguration?> FindAsync(
        Guid providerId,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.AsNoTracking().SingleAsync(ct);
        var provider = settings.SsoConfiguration.Providers
            .SingleOrDefault(item => item.Id == providerId);
        if (provider is null)
            return null;
        return new(
            settings.SsoConfiguration.Enabled,
            settings.SsoConfiguration.PublicBaseUrl,
            provider.Id,
            provider.Name,
            provider.Protocol,
            provider.Enabled,
            provider.AllowLogin,
            provider.AllowBinding,
            provider.TimeoutSeconds,
            provider.AllowedHosts,
            Fingerprint(provider, settings.SsoConfiguration.PublicBaseUrl),
            provider.Oidc is null ? null : new OidcSsoRuntimeConfiguration(
                provider.Oidc.Issuer,
                provider.Oidc.DiscoveryUrl,
                provider.Oidc.ClientId,
                provider.Oidc.ClientSecretCiphertext is { Length: > 0 }
                    ? secrets.Unprotect(
                        provider.Oidc.ClientSecretCiphertext,
                        PlatformSecretPurpose.SsoOidcClientSecret,
                        provider.Id)
                    : string.Empty,
                provider.Oidc.Scopes,
                provider.Oidc.ReadUserInfo,
                provider.Oidc.DisplayNameClaim),
            provider.Cas is null ? null : new CasSsoRuntimeConfiguration(
                provider.Cas.IdentityNamespace,
                provider.Cas.LoginUrl,
                provider.Cas.ServiceValidateUrl,
                provider.Cas.DisplayNameAttribute));
    }

    internal static string Fingerprint(
        SsoProviderConfiguration provider,
        string publicBaseUrl)
    {
        var boundary = provider.Protocol switch
        {
            SsoProtocol.Oidc when provider.Oidc is not null =>
                $"oidc\n{provider.Oidc.Issuer}\n{provider.Oidc.DiscoveryUrl}\n{provider.Oidc.ClientId}",
            SsoProtocol.Cas when provider.Cas is not null =>
                $"cas\n{provider.Cas.IdentityNamespace}\n{provider.Cas.LoginUrl}\n{provider.Cas.ServiceValidateUrl}",
            _ => throw new InvalidOperationException("The SSO provider protocol configuration is incomplete.")
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{publicBaseUrl}\n{boundary}")));
    }
}

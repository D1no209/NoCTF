using NoCTF.Domain.Identity;

namespace NoCTF.Domain.Platform;

public sealed class SsoConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public List<SsoProviderConfiguration> Providers { get; set; } = [];
}

public sealed class SsoProviderConfiguration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public SsoProtocol Protocol { get; set; }
    public bool Enabled { get; set; }
    public bool AllowLogin { get; set; }
    public bool AllowBinding { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public string[] AllowedHosts { get; set; } = [];
    public OidcSsoProviderConfiguration? Oidc { get; set; }
    public CasSsoProviderConfiguration? Cas { get; set; }
}

public sealed class OidcSsoProviderConfiguration
{
    public string Issuer { get; set; } = string.Empty;
    public string DiscoveryUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public byte[]? ClientSecretCiphertext { get; set; }
    public string[] Scopes { get; set; } = ["openid", "profile"];
    public bool ReadUserInfo { get; set; }
    public string DisplayNameClaim { get; set; } = "name";
}

public sealed class CasSsoProviderConfiguration
{
    public string IdentityNamespace { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string ServiceValidateUrl { get; set; } = string.Empty;
    public string DisplayNameAttribute { get; set; } = "displayName";
}

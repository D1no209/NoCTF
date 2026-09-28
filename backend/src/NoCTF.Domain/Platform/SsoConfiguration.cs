using NoCTF.Domain.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;
using System.Text.Json.Serialization;

namespace NoCTF.Domain.Platform;

public sealed class SsoConfiguration
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
    public List<SsoProviderConfiguration> Providers { get; set; } = [];
}

[PersistentHierarchy]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OidcSsoProviderConfiguration), "oidc")]
[JsonDerivedType(typeof(CasSsoProviderConfiguration), "cas")]
public abstract class SsoProviderConfiguration
{
    protected SsoProviderConfiguration(SsoProtocol protocol) => Protocol = protocol;
    public Guid Id { get; set; }
    public short PlatformSettingsId { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public SsoProtocol Protocol { get; private set; }
    public bool Enabled { get; set; }
    public bool AllowLogin { get; set; }
    public bool AllowBinding { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public List<SsoProviderAllowedHost> AllowedHostEntries { get; set; } = [];
    [NotMapped]
    public string[] AllowedHosts
    {
        get => AllowedHostEntries.OrderBy(entry => entry.Position).Select(entry => entry.Value).ToArray();
        set => AllowedHostEntries = (value ?? [])
            .Select((host, position) => new SsoProviderAllowedHost
            {
                SsoProviderId = this.Id,
                Position = position,
                Value = host
            }).ToList();
    }
    [NotMapped, JsonIgnore]
    public OidcSsoProviderConfiguration? Oidc => this as OidcSsoProviderConfiguration;
    [NotMapped, JsonIgnore]
    public CasSsoProviderConfiguration? Cas => this as CasSsoProviderConfiguration;
}

[PersistentDiscriminator("oidc")]
public sealed class OidcSsoProviderConfiguration()
    : SsoProviderConfiguration(SsoProtocol.Oidc)
{
    public string Issuer { get; set; } = string.Empty;
    public string DiscoveryUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public byte[]? ClientSecretCiphertext { get; set; }
    public List<OidcSsoScope> ScopeEntries { get; set; } = [];
    [NotMapped]
    public string[] Scopes
    {
        get => ScopeEntries.OrderBy(entry => entry.Position).Select(entry => entry.Value).ToArray();
        set => ScopeEntries = (value ?? [])
            .Select((scope, position) => new OidcSsoScope
            {
                SsoProviderId = this.Id,
                Position = position,
                Value = scope
            }).ToList();
    }
    public bool ReadUserInfo { get; set; }
    public string DisplayNameClaim { get; set; } = "name";
}

public sealed class SsoProviderAllowedHost
{
    public Guid SsoProviderId { get; set; }
    public int Position { get; set; }
    [MaxLength(253)] public string Value { get; set; } = string.Empty;
}

public sealed class OidcSsoScope
{
    public Guid SsoProviderId { get; set; }
    public int Position { get; set; }
    [MaxLength(128)] public string Value { get; set; } = string.Empty;
}

[PersistentDiscriminator("cas")]
public sealed class CasSsoProviderConfiguration()
    : SsoProviderConfiguration(SsoProtocol.Cas)
{
    public string IdentityNamespace { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string ServiceValidateUrl { get; set; } = string.Empty;
    public string DisplayNameAttribute { get; set; } = "displayName";
}

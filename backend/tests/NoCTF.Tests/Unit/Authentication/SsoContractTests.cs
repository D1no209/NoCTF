using System.Text.Json;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class SsoContractTests
{
    [Test]
    public async Task Default_configuration_is_disabled_and_versioned()
    {
        var configuration = new SsoConfiguration();

        await Assert.That(configuration.SchemaVersion).IsEqualTo(1);
        await Assert.That(configuration.Enabled).IsFalse();
        await Assert.That(configuration.Providers).IsEmpty();
    }

    [Test]
    public async Task Provider_document_round_trips_with_typed_protocol_settings()
    {
        var providerId = Guid.Parse("01990f88-0c6e-7adc-8aa2-09de8868521b");
        var document = new SsoConfiguration
        {
            Enabled = true,
            PublicBaseUrl = "https://ctf.example.test",
            Providers =
            [
                new SsoProviderConfiguration
                {
                    Id = providerId,
                    Name = "Example OIDC",
                    Protocol = SsoProtocol.Oidc,
                    Enabled = true,
                    AllowLogin = true,
                    AllowBinding = true,
                    AllowedHosts = ["id.example.test"],
                    Oidc = new OidcSsoProviderConfiguration
                    {
                        Issuer = "https://id.example.test",
                        DiscoveryUrl = "https://id.example.test/.well-known/openid-configuration",
                        ClientId = "noctf",
                        ClientSecretCiphertext = [1, 2, 3],
                        Scopes = ["openid", "profile"]
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<SsoConfiguration>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Providers).Count().IsEqualTo(1);
        await Assert.That(restored.Providers[0].Id).IsEqualTo(providerId);
        await Assert.That(restored.Providers[0].Protocol).IsEqualTo(SsoProtocol.Oidc);
        await Assert.That(restored.Providers[0].Oidc!.ClientSecretCiphertext)
            .IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(restored.Providers[0].Cas).IsNull();
    }

    [Test]
    public async Task Lifetimes_and_collection_limits_are_frozen()
    {
        var limits = new[] { SsoRules.MaximumProviders, SsoRules.MaximumSubjectLength };
        await Assert.That(limits).IsEquivalentTo(new[] { 16, 255 });
        await Assert.That(SsoRules.FlowLifetime).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(SsoRules.ResultLifetime).IsEqualTo(TimeSpan.FromMinutes(2));
    }
}

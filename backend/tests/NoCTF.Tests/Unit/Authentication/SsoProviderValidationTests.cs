using System.Net;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class SsoProviderValidationTests
{
    [Test]
    public async Task Oidc_requires_https_openid_scope_and_matching_allowed_hosts()
    {
        var valid = OidcDraft();

        await Assert.That(SsoProviderValidation.IsValid(valid, allowHttp: false)).IsTrue();
        await Assert.That(SsoProviderValidation.IsValid(
            valid with
            {
                Oidc = valid.Oidc! with { DiscoveryUrl = "http://id.example.test/metadata" }
            },
            allowHttp: false)).IsFalse();
        await Assert.That(SsoProviderValidation.IsValid(
            valid with
            {
                Oidc = valid.Oidc! with { Scopes = ["profile"] }
            },
            allowHttp: false)).IsFalse();
        await Assert.That(SsoProviderValidation.IsValid(
            valid with { AllowedHosts = ["other.example.test"] },
            allowHttp: false)).IsFalse();
    }

    [Test]
    public async Task Protocol_configuration_must_be_exclusive()
    {
        var oidc = OidcDraft();
        var both = oidc with
        {
            Cas = new(
                "example-cas",
                "https://cas.example.test/login",
                "https://cas.example.test/serviceValidate",
                "displayName")
        };

        await Assert.That(SsoProviderValidation.IsValid(both, allowHttp: false)).IsFalse();
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.1")]
    [Arguments("172.20.0.1")]
    [Arguments("192.168.1.1")]
    [Arguments("169.254.169.254")]
    [Arguments("::1")]
    [Arguments("fd00::1")]
    public async Task Backchannel_blocks_non_public_addresses_by_default(string address)
    {
        await Assert.That(SsoBackchannel.IsBlocked(IPAddress.Parse(address))).IsTrue();
    }

    [Test]
    public async Task Client_secret_ciphertext_is_bound_to_provider_id()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var protector = new PlatformSecretProtector(Options.Create(
            new EmailVerificationProtectionOptions { EncryptionKey = key }));
        var first = Guid.Parse("01990f8f-e4bb-7f6f-adde-6452d49c3941");
        var second = Guid.Parse("01990f8f-e4bb-7f6f-adde-6452d49c3942");
        var ciphertext = protector.Protect(
            "provider-secret",
            PlatformSecretPurpose.SsoOidcClientSecret,
            first);

        await Assert.That(protector.Unprotect(
            ciphertext,
            PlatformSecretPurpose.SsoOidcClientSecret,
            first)).IsEqualTo("provider-secret");
        await Assert.That(() => protector.Unprotect(
            ciphertext,
            PlatformSecretPurpose.SsoOidcClientSecret,
            second)).Throws<System.Security.Cryptography.AuthenticationTagMismatchException>();
    }

    private static SsoProviderDraft OidcDraft() => new(
        "Example OIDC",
        SsoProtocol.Oidc,
        Enabled: false,
        AllowLogin: true,
        AllowBinding: true,
        TimeoutSeconds: 10,
        AllowedHosts: ["id.example.test"],
        Oidc: new(
            "https://id.example.test",
            "https://id.example.test/.well-known/openid-configuration",
            "noctf",
            ["openid", "profile"],
            ReadUserInfo: false,
            "name"),
        Cas: null);
}

using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class MfaCoreTests
{
    [Test]
    [Arguments(UserKind.Human, UserRole.User, MfaPolicy.Optional, false, false, false)]
    [Arguments(UserKind.Human, UserRole.User, MfaPolicy.RequirePrivileged, false, true, true)]
    [Arguments(UserKind.Human, UserRole.Organizer, MfaPolicy.RequirePrivileged, false, false, true)]
    [Arguments(UserKind.Human, UserRole.Administrator, MfaPolicy.RequirePrivileged, false, false, true)]
    [Arguments(UserKind.Human, UserRole.User, MfaPolicy.Optional, true, false, true)]
    [Arguments(UserKind.Human, UserRole.User, MfaPolicy.RequireAllHumanUsers, false, false, true)]
    [Arguments(UserKind.Bot, UserRole.Administrator, MfaPolicy.RequireAllHumanUsers, true, true, false)]
    [Arguments(UserKind.Bot, UserRole.Organizer, MfaPolicy.RequirePrivileged, true, true, false)]
    public async Task Policy_always_excludes_Bots(UserKind kind, UserRole role, MfaPolicy policy, bool accountRequired, bool competitionPrivileged, bool expected) =>
        await Assert.That(MfaRequirements.IsMandated(kind, role, accountRequired, policy, competitionPrivileged)).IsEqualTo(expected);

    [Test]
    public async Task Enrolled_optional_accounts_require_Mfa_but_can_disable_it()
    {
        await Assert.That(MfaRequirements.IsRequired(UserKind.Human, UserRole.User, false, MfaPolicy.Optional, false, true)).IsTrue();
        await Assert.That(MfaRequirements.IsMandated(UserKind.Human, UserRole.User, false, MfaPolicy.Optional, false)).IsFalse();
        await Assert.That(MfaRequirements.IsRequired(UserKind.Bot, UserRole.Administrator, true, MfaPolicy.RequireAllHumanUsers, true, true)).IsFalse();
    }

    [Test]
    [Arguments(59L, "287082")]
    [Arguments(1111111109L, "081804")]
    [Arguments(1111111111L, "050471")]
    [Arguments(1234567890L, "005924")]
    [Arguments(2000000000L, "279037")]
    [Arguments(20000000000L, "353130")]
    public async Task Totp_matches_the_Rfc_Sha1_vectors(long unixTime, string code)
    {
        var crypto = new MfaCryptography();
        var accepted = crypto.VerifyTotp("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", code, DateTimeOffset.FromUnixTimeSeconds(unixTime), out var step);
        await Assert.That(accepted).IsTrue();
        await Assert.That(step).IsEqualTo(unixTime / 30);
    }

    [Test]
    public async Task Recovery_codes_have_128_bit_material_and_canonical_hashes()
    {
        var crypto = new MfaCryptography();
        var codes = crypto.GenerateRecoveryCodes();
        await Assert.That(codes.Count).IsEqualTo(10);
        await Assert.That(codes.Distinct().Count()).IsEqualTo(10);
        foreach (var code in codes)
        {
            await Assert.That(code.Split('-').All(group => group.Length == 8)).IsTrue();
            await Assert.That(crypto.HashRecoveryCode(code)!.SequenceEqual(crypto.HashRecoveryCode(code.ToLowerInvariant().Replace("-", ""))!)).IsTrue();
        }
        await Assert.That(crypto.HashRecoveryCode("short")).IsNull();
    }

    [Test]
    public async Task Secret_encryption_binds_the_user_credential_and_purpose()
    {
        var protector = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        var user = Guid.NewGuid(); var credential = Guid.NewGuid();
        var ciphertext = protector.Protect("secret", PlatformSecretPurpose.TotpCredentialSecret, user, credential);
        await Assert.That(protector.Unprotect(ciphertext, PlatformSecretPurpose.TotpCredentialSecret, user, credential)).IsEqualTo("secret");
        await Assert.That(() => protector.Unprotect(ciphertext, PlatformSecretPurpose.TotpCredentialSecret, Guid.NewGuid(), credential)).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(ciphertext, PlatformSecretPurpose.PendingTotpSecret, user, credential)).Throws<CryptographicException>();
    }

    [Test]
    public async Task Oidc_requires_fresh_signed_token_proof_and_exact_rules()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        var policy = new OidcMfaTrust(true, Guid.NewGuid(), 300, ["high"], [["pwd", "otp"]]);
        var provider = Guid.NewGuid();
        await Assert.That(policy.Verify(provider, "high", [], now.AddMinutes(-1), now)).IsNotNull();
        await Assert.That(policy.Verify(provider, null, ["pwd", "otp", "extra"], now, now)).IsNotNull();
        await Assert.That(policy.Verify(provider, "HIGH", ["mfa"], now, now)).IsNull();
        await Assert.That(policy.Verify(provider, "high", [], null, now)).IsNull();
        await Assert.That(policy.Verify(provider, "high", [], now.AddMinutes(-6), now)).IsNull();
        await Assert.That((policy with { Enabled = false }).Verify(provider, "high", [], now, now)).IsNull();
    }
}

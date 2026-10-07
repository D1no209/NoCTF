using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace NoCTF.Infrastructure.Authentication;

public enum PlatformSecretPurpose
{
    EmailSmtpPassword,
    HumanVerificationCapSecret,
    HumanVerificationTurnstileSecret,
    SsoOidcClientSecret,
    SsoDataProtectionKey,
    CompetitionWebhookSecret,
    TotpCredentialSecret,
    PendingTotpSecret,
    MfaRecoveryGrant
}

public sealed class PlatformSecretProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly string? configuredKey;

    public PlatformSecretProtector(
        IOptions<EmailVerificationProtectionOptions> options)
    {
        configuredKey = options.Value.EncryptionKey;
    }

    public byte[] Protect(
        string secret,
        PlatformSecretPurpose purpose,
        Guid? scopeId = null)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var key = ReadKey();
        try
        {
            var ciphertext = new byte[plaintext.Length];
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var tag = new byte[TagLength];
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(purpose, scopeId));

            var protectedSecret = new byte[1 + NonceLength + TagLength + ciphertext.Length];
            protectedSecret[0] = 1;
            nonce.CopyTo(protectedSecret.AsSpan(1, NonceLength));
            tag.CopyTo(protectedSecret.AsSpan(1 + NonceLength, TagLength));
            ciphertext.CopyTo(protectedSecret.AsSpan(1 + NonceLength + TagLength));
            return protectedSecret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Unprotect(
        byte[] protectedSecret,
        PlatformSecretPurpose purpose,
        Guid? scopeId = null)
    {
        if (protectedSecret.Length <= 1 + NonceLength + TagLength
            || protectedSecret[0] != 1)
            throw new CryptographicException("The protected platform secret is invalid.");

        var nonce = protectedSecret.AsSpan(1, NonceLength);
        var tag = protectedSecret.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = protectedSecret.AsSpan(1 + NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];
        var key = ReadKey();
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(purpose, scopeId));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Protect(
        string secret,
        PlatformSecretPurpose purpose,
        Guid primaryScopeId,
        Guid secondaryScopeId) =>
        ProtectCore(secret, purpose, primaryScopeId, secondaryScopeId);

    public string Unprotect(
        byte[] protectedSecret,
        PlatformSecretPurpose purpose,
        Guid primaryScopeId,
        Guid secondaryScopeId) =>
        UnprotectCore(protectedSecret, purpose, primaryScopeId, secondaryScopeId);

    private byte[] ProtectCore(
        string secret,
        PlatformSecretPurpose purpose,
        Guid? primaryScopeId,
        Guid? secondaryScopeId)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var key = ReadKey();
        try
        {
            var ciphertext = new byte[plaintext.Length];
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var tag = new byte[TagLength];
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag,
                AssociatedData(purpose, primaryScopeId, secondaryScopeId));

            var protectedSecret = new byte[1 + NonceLength + TagLength + ciphertext.Length];
            protectedSecret[0] = 1;
            nonce.CopyTo(protectedSecret.AsSpan(1, NonceLength));
            tag.CopyTo(protectedSecret.AsSpan(1 + NonceLength, TagLength));
            ciphertext.CopyTo(protectedSecret.AsSpan(1 + NonceLength + TagLength));
            return protectedSecret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private string UnprotectCore(
        byte[] protectedSecret,
        PlatformSecretPurpose purpose,
        Guid? primaryScopeId,
        Guid? secondaryScopeId)
    {
        if (protectedSecret.Length <= 1 + NonceLength + TagLength
            || protectedSecret[0] != 1)
            throw new CryptographicException("The protected platform secret is invalid.");

        var nonce = protectedSecret.AsSpan(1, NonceLength);
        var tag = protectedSecret.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = protectedSecret.AsSpan(1 + NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];
        var key = ReadKey();
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext,
                AssociatedData(purpose, primaryScopeId, secondaryScopeId));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] ReadKey()
    {
        try
        {
            var key = Convert.FromBase64String(configuredKey ?? string.Empty);
            if (key.Length == 32)
                return key;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "EmailVerification:EncryptionKey must be a Base64-encoded 32-byte key.",
                exception);
        }

        throw new InvalidOperationException(
            "EmailVerification:EncryptionKey must be a Base64-encoded 32-byte key.");
    }

    private static byte[] AssociatedData(
        PlatformSecretPurpose purpose,
        Guid? scopeId,
        Guid? secondaryScopeId = null)
    {
        var prefix = purpose switch
        {
            PlatformSecretPurpose.EmailSmtpPassword =>
                "NoCTF.EmailVerification.SmtpPassword.v1",
            PlatformSecretPurpose.HumanVerificationCapSecret =>
                "NoCTF.HumanVerification.CapSecret.v1",
            PlatformSecretPurpose.HumanVerificationTurnstileSecret =>
                "NoCTF.HumanVerification.TurnstileSecret.v1",
            PlatformSecretPurpose.SsoOidcClientSecret =>
                "NoCTF.Sso.OidcClientSecret.v1",
            PlatformSecretPurpose.SsoDataProtectionKey =>
                "NoCTF.Sso.DataProtectionKey.v1",
            PlatformSecretPurpose.CompetitionWebhookSecret =>
                "NoCTF.Competition.WebhookSecret.v1",
            PlatformSecretPurpose.TotpCredentialSecret => "NoCTF.Authentication.TotpCredential.v1",
            PlatformSecretPurpose.PendingTotpSecret => "NoCTF.Authentication.PendingTotp.v1",
            PlatformSecretPurpose.MfaRecoveryGrant => "NoCTF.Authentication.MfaRecoveryGrant.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
        };
        return Encoding.UTF8.GetBytes(scopeId is null
            ? prefix
            : secondaryScopeId is null
                ? $"{prefix}:{scopeId.Value:N}"
                : $"{prefix}:{scopeId.Value:N}:{secondaryScopeId.Value:N}");
    }
}

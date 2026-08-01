using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EmailVerificationSecretProtector
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] AssociatedData =
        Encoding.UTF8.GetBytes("NoCTF.EmailVerification.SmtpPassword.v1");
    private readonly string? configuredKey;

    public EmailVerificationSecretProtector(IConfiguration configuration)
    {
        configuredKey = configuration["EmailVerification:EncryptionKey"];
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

    public byte[] Protect(string secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var key = ReadKey();
        try
        {
            var ciphertext = new byte[plaintext.Length];
            var nonce = RandomNumberGenerator.GetBytes(NonceLength);
            var tag = new byte[TagLength];
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

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

    public string Unprotect(byte[] protectedSecret)
    {
        if (protectedSecret.Length <= 1 + NonceLength + TagLength || protectedSecret[0] != 1)
            throw new CryptographicException("The SMTP password payload is invalid.");

        var nonce = protectedSecret.AsSpan(1, NonceLength);
        var tag = protectedSecret.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = protectedSecret.AsSpan(1 + NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];
        var key = ReadKey();
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

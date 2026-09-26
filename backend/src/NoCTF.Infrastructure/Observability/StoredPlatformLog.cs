using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Administration.PlatformLogs;

namespace NoCTF.Infrastructure.Observability;

/// <summary>Internal Loki document; user identifiers are encrypted, never stored as plaintext.</summary>
public sealed record StoredPlatformLog(
    PlatformLogView View,
    string? EncryptedUserId);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StoredPlatformLog))]
public partial class StoredPlatformLogJsonContext : JsonSerializerContext;

public sealed class PlatformLogUserIdProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int IdSize = 16;
    private readonly byte[] key;

    public PlatformLogUserIdProtector(IConfiguration configuration)
    {
        var sharedSecret = configuration["RunnerScoring:SigningKey"];
        if (string.IsNullOrWhiteSpace(sharedSecret)
            || Encoding.UTF8.GetByteCount(sharedSecret) < 32)
            throw new InvalidOperationException(
                "RunnerScoring:SigningKey is required for protected platform logs.");
        key = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(sharedSecret),
            "NoCTF.PlatformLogs.UserId.v1"u8.ToArray());
    }

    public string Protect(Guid userId)
    {
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> plaintext = stackalloc byte[IdSize];
        Span<byte> ciphertext = stackalloc byte[IdSize];
        Span<byte> tag = stackalloc byte[TagSize];
        RandomNumberGenerator.Fill(nonce);
        if (!userId.TryWriteBytes(plaintext))
            throw new InvalidOperationException("Could not encode the user identifier.");
        using (var cipher = new AesGcm(key, TagSize))
            cipher.Encrypt(nonce, plaintext, ciphertext, tag);
        Span<byte> packed = stackalloc byte[NonceSize + IdSize + TagSize];
        nonce.CopyTo(packed);
        ciphertext.CopyTo(packed[NonceSize..]);
        tag.CopyTo(packed[(NonceSize + IdSize)..]);
        return Convert.ToBase64String(packed);
    }

    public Guid Unprotect(string encryptedUserId)
    {
        var packed = Convert.FromBase64String(encryptedUserId);
        if (packed.Length != NonceSize + IdSize + TagSize)
            throw new CryptographicException("The protected user identifier is invalid.");
        Span<byte> plaintext = stackalloc byte[IdSize];
        using (var cipher = new AesGcm(key, TagSize))
            cipher.Decrypt(
                packed.AsSpan(0, NonceSize),
                packed.AsSpan(NonceSize, IdSize),
                packed.AsSpan(NonceSize + IdSize, TagSize),
                plaintext);
        return new Guid(plaintext);
    }
}

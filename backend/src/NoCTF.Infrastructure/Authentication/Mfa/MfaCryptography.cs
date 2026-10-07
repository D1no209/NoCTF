using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Authentication.Mfa;
using OtpNet;

namespace NoCTF.Infrastructure.Authentication.Mfa;

public sealed class MfaCryptography : IMfaCryptography
{
    public string GenerateSecret() => Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));

    public bool VerifyTotp(string secret, string code, DateTimeOffset now, out long matchedStep)
    {
        matchedStep = -1;
        if (code.Length != 6 || code.Any(character => character is < '0' or > '9')) return false;
        var key = Base32Encoding.ToBytes(secret);
        try
        {
            return new Totp(key, step: 30, mode: OtpHashMode.Sha1, totpSize: 6)
                .VerifyTotp(now.UtcDateTime, code, out matchedStep, new VerificationWindow(1, 1));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public IReadOnlyList<string> GenerateRecoveryCodes() => Enumerable.Range(0, 10)
        .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Select(value => string.Join('-', Enumerable.Range(0, 4).Select(index => value.Substring(index * 8, 8))))
        .ToArray();

    public byte[]? HashRecoveryCode(string code)
    {
        var normalized = code.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Length != 32 || normalized.Any(character => !(character is >= '0' and <= '9' or >= 'A' and <= 'F'))) return null;
        return SHA256.HashData(Encoding.ASCII.GetBytes(normalized));
    }

    public string GenerateBrowserSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public string HashBrowserSecret(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

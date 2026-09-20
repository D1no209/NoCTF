using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace NoCTF.API.Security;

public sealed class SsoBrowserCorrelation
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector protector;
    private readonly TimeProvider clock;
    private readonly string cookieName;
    private readonly bool secure;

    public SsoBrowserCorrelation(
        IDataProtectionProvider protection,
        TimeProvider clock,
        IWebHostEnvironment environment)
    {
        protector = protection.CreateProtector("NoCTF.Sso.BrowserCorrelation.v1");
        this.clock = clock;
        secure = !environment.IsDevelopment();
        cookieName = secure ? "__Host-NoCTF.Sso" : "NoCTF.Sso.Dev";
    }

    public string GetOrCreate(HttpContext context)
    {
        var current = Read(context);
        if (current is not null)
            return current;
        var browserId = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expiresAt = clock.GetUtcNow().Add(Lifetime);
        var value = protector.Protect($"{expiresAt.ToUnixTimeSeconds()}:{browserId}");
        context.Response.Cookies.Append(cookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true
        });
        return browserId;
    }

    public string? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(cookieName, out var value)
            || string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            var plaintext = protector.Unprotect(value);
            var separator = plaintext.IndexOf(':');
            if (separator <= 0
                || !long.TryParse(plaintext[..separator], out var expiresAt)
                || DateTimeOffset.FromUnixTimeSeconds(expiresAt) <= clock.GetUtcNow())
                return null;
            var browserId = plaintext[(separator + 1)..];
            return browserId.Length is >= 32 and <= 64 ? browserId : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string Hash(string browserId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(browserId)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

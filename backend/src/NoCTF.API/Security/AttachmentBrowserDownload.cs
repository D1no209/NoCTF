using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NoCTF.API.Security;

/// <summary>Only explicitly marked attachment GET routes can read the short-lived browser handoff cookie.</summary>
public sealed record AttachmentBrowserDownloadMetadata;

public sealed class AttachmentBrowserDownload(IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment environment)
{
    private readonly IDataProtector protector = protection.CreateProtector("NoCTF.AttachmentBrowserDownload.v1");
    public void Write(HttpContext context, string path)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Browser attachment handoff requires an access Bearer token.");
        var expires = clock.GetUtcNow().AddMinutes(2);
        var grant = protector.Protect(JsonSerializer.Serialize(new Grant(path, authorization[7..], expires)));
        context.Response.Cookies.Append(Name(path), grant, new CookieOptions { HttpOnly = true,
            Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = path, Expires = expires, IsEssential = true });
        context.Response.Headers.CacheControl = "private, no-store";
    }
    public string? Read(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.ContainsKey("Authorization")
            || context.GetEndpoint()?.Metadata.GetMetadata<AttachmentBrowserDownloadMetadata>() is null) return null;
        var path = context.Request.Path.Value!;
        if (!context.Request.Cookies.TryGetValue(Name(path), out var cookie)) return null;
        try
        {
            var grant = JsonSerializer.Deserialize<Grant>(protector.Unprotect(cookie));
            if (grant is null || grant.Path != path || grant.ExpiresAt <= clock.GetUtcNow()) return null;
            context.Response.Cookies.Delete(Name(path), new CookieOptions { Path = path, HttpOnly = true,
                Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict });
            return grant.AccessToken;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return null; }
    }
    private string Name(string path) => (environment.IsDevelopment() ? "NoCTF.Attachment." : "__Secure-NoCTF.Attachment.")
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
    private sealed record Grant(string Path, string AccessToken, DateTimeOffset ExpiresAt);
}

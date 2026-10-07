using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NoCTF.API.Security;

public sealed record WriteUpBrowserAccessMetadata;

/// <summary>A narrowly scoped JWT handoff for PDF.js range requests and native downloads.</summary>
public sealed class WriteUpBrowserAccess(IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment environment)
{
    private readonly IDataProtector protector = protection.CreateProtector("NoCTF.ChallengeWriteUpBrowserAccess.v1");
    public void Write(HttpContext context, string path)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WriteUp browser handoff requires an access Bearer token.");
        var expires = clock.GetUtcNow().AddMinutes(5);
        context.Response.Cookies.Append(Name(path), protector.Protect(JsonSerializer.Serialize(new Grant(path, authorization[7..], expires))),
            new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict,
                Path = path, Expires = expires, IsEssential = true });
        context.Response.Headers.CacheControl = "private, no-store";
    }
    public string? Read(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.ContainsKey("Authorization")
            || context.GetEndpoint()?.Metadata.GetMetadata<WriteUpBrowserAccessMetadata>() is null) return null;
        var path = context.Request.Path.Value!;
        if (!context.Request.Cookies.TryGetValue(Name(path), out var cookie)) return null;
        try
        {
            var grant = JsonSerializer.Deserialize<Grant>(protector.Unprotect(cookie));
            return grant?.Path == path && grant.ExpiresAt > clock.GetUtcNow() ? grant.AccessToken : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return null; }
    }
    private string Name(string path) => (environment.IsDevelopment() ? "NoCTF.WriteUp." : "__Secure-NoCTF.WriteUp.")
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
    private sealed record Grant(string Path, string AccessToken, DateTimeOffset ExpiresAt);
}

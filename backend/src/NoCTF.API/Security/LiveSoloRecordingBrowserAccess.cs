using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NoCTF.API.Security;

public sealed record LiveSoloRecordingBrowserAccessMetadata;

/// <summary>File-bound access JWT handoff. Every byte-range request still runs normal JWT and current MFA validation.</summary>
public sealed class LiveSoloRecordingBrowserAccess(IDataProtectionProvider protection, TimeProvider clock, IWebHostEnvironment environment)
{
    private readonly IDataProtector protector = protection.CreateProtector("NoCTF.LiveSoloRecordingBrowserAccess.v1");
    private static readonly object FileBinding = new();
    public static bool MatchesFile(HttpContext context, Guid fileId) =>
        !context.Items.TryGetValue(FileBinding, out var expected) || expected is Guid id && id == fileId;
    public DateTimeOffset Write(HttpContext context, string path, Guid fileId)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recording preview requires an access Bearer token.");
        var expires = clock.GetUtcNow().AddMinutes(5);
        context.Response.Cookies.Append(Name(path), protector.Protect(JsonSerializer.Serialize(new Grant(path, authorization[7..], expires, fileId))),
            new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict,
                Path = path, Expires = expires, IsEssential = true });
        context.Response.Headers.CacheControl = "private, no-store"; return expires;
    }
    public string? Read(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.ContainsKey("Authorization")
            || context.GetEndpoint()?.Metadata.GetMetadata<LiveSoloRecordingBrowserAccessMetadata>() is null) return null;
        var path = context.Request.Path.Value!;
        if (!context.Request.Cookies.TryGetValue(Name(path), out var cookie)) return null;
        try
        {
            var grant = JsonSerializer.Deserialize<Grant>(protector.Unprotect(cookie));
            if (grant?.Path != path || grant.ExpiresAt <= clock.GetUtcNow()) return null;
            context.Items[FileBinding] = grant.FileId; return grant.AccessToken;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return null; }
    }
    private string Name(string path) => (environment.IsDevelopment() ? "NoCTF.LiveSoloRecording." : "__Secure-NoCTF.LiveSoloRecording.")
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
    private sealed record Grant(string Path, string AccessToken, DateTimeOffset ExpiresAt, Guid FileId);
}

using NoCTF.Application.Authentication.Passkeys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class PasskeyBrowserFlow(IWebHostEnvironment environment)
{
    private string Name => environment.IsDevelopment() ? "NoCTF.Passkey.Dev" : "__Host-NoCTF.Passkey";
    public PasskeyBrowserCredential? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Name, out var value)) return null;
        var parts = value.Split('.', 2);
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) && parts[1].Length == 43 ? new(id, parts[1]) : null;
    }
    public void Write(HttpContext context, PasskeyStartedCeremony value) => context.Response.Cookies.Append(Name,
        $"{value.Browser.CeremonyId:N}.{value.Browser.Secret}", Options(value.ExpiresAt));
    public void Clear(HttpContext context) => context.Response.Cookies.Delete(Name, Options(null));
    private CookieOptions Options(DateTimeOffset? expiry) => new() { HttpOnly = true, Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict, Path = "/", Expires = expiry, IsEssential = true };
    public static string? Origin(HttpRequest request, bool allowHeaderlessRead = false)
    {
        var origins = request.Headers.Origin;
        if (origins.Count > 1) return null;
        var value = origins.FirstOrDefault() ?? request.Headers.Referer.FirstOrDefault();
        if (string.IsNullOrEmpty(value) && allowHeaderlessRead) value = request.Scheme + "://" + request.Host;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            && uri.UserInfo.Length == 0 ? uri.GetLeftPart(UriPartial.Authority) : null;
    }
}

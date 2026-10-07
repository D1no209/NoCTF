using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Security;

public sealed class MfaBrowserFlow(IWebHostEnvironment environment)
{
    private string Name => environment.IsDevelopment() ? "NoCTF.Mfa.Dev" : "__Host-NoCTF.Mfa";
    public MfaBrowserCredential? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Name, out var value)) return null;
        var pieces = value.Split('.', 2);
        return pieces.Length == 2 && Guid.TryParseExact(pieces[0], "N", out var id) && pieces[1].Length == 43
            ? new(id, pieces[1]) : null;
    }
    public void Write(HttpContext context, MfaBrowserCredential browser, DateTimeOffset expiresAt) => context.Response.Cookies.Append(Name,
        $"{browser.ChallengeId:N}.{browser.Secret}", new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict, Path = "/", Expires = expiresAt, IsEssential = true });
    public void Clear(HttpContext context) => context.Response.Cookies.Delete(Name,
        new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = "/" });
}

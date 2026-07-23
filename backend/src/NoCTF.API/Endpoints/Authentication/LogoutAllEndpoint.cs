using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class LogoutAllEndpoint(LogoutAll logoutAll, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Post("/auth/logout-all");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Invalidate every user token";
            summary.Description = "Increments TokenVersion and clears the current refresh cookie.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await logoutAll.ExecuteAsync(user.UserId, DateTimeOffset.UtcNow, ct);
        if (!result.Succeeded)
            return TypedResults.NotFound();
        HttpContext.Response.Cookies.Delete(
            "__Secure-noctf_refresh",
            new CookieOptions { Path = "/api/v1/auth" });
        return TypedResults.NoContent();
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class UnbindSsoIdentityEndpoint(
    UnbindSsoIdentity unbind,
    IUserContext user,
    IOptions<RefreshHttpOptions> refreshOptions)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/auth/me/sso-binding");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.Authentication)));
        Description(builder => builder.WithName("Authentication_SsoUnbindIdentity"));
        Summary(summary => summary.Summary = "Removes the authenticated user's external identity binding and revokes current sessions.");
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return SsoEndpointProblems.Create(SsoFailureCode.AccountUnavailable);
        var result = await unbind.ExecuteAsync(user.UserId, ct);
        if (!result.Succeeded)
            return SsoEndpointProblems.Create(result.FailureCode!.Value);
        HttpContext.Response.Cookies.Delete(
            RefreshCookie.Name(refreshOptions.Value),
            RefreshCookie.DeleteOptions(refreshOptions.Value));
        return TypedResults.NoContent();
    }
}

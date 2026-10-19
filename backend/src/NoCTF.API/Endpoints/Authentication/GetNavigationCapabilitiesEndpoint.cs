using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record NavigationCapabilitiesResponse(bool CanAccessTheoryBank, bool HasManagedCompetitions);

public sealed class GetNavigationCapabilitiesEndpoint(GetNavigationCapabilities get, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<NavigationCapabilitiesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/navigation-capabilities");
        AuthSchemes("Bearer");
        Summary(value => value.Summary = "Get the current user's navigation capabilities.");
    }

    public override async Task<Results<Ok<NavigationCapabilitiesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var value = await get.ExecuteAsync(user.UserId, ct);
        return value is null ? TypedResults.Unauthorized()
            : TypedResults.Ok(new NavigationCapabilitiesResponse(value.CanAccessTheoryBank, value.HasManagedCompetitions));
    }
}

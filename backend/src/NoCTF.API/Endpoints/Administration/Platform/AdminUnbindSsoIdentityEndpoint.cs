using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class AdminUnbindSsoIdentityEndpoint(
    AdministrativelyUnbindSsoIdentity unbind,
    IUserContext actor)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/platform/users/{userId:guid}/sso-binding");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUnbindSsoIdentity"));
        Summary(summary => { summary.Summary = "Removes a user's external identity binding and revokes their sessions."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await unbind.ExecuteAsync(
            Route<Guid>("userId"),
            actor.UserId,
            ct);
        return result switch
        {
            AdminSsoUnbindState.Unbound => TypedResults.NoContent(),
            AdminSsoUnbindState.UserNotFound => TypedResults.NotFound(),
            _ => SsoEndpointProblems.Create(SsoFailureCode.IdentityNotLinked)
        };
    }
}

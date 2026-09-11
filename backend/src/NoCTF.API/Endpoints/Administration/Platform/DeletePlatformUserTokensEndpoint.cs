using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class DeletePlatformUserTokensEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<PlatformUserResponse>, NotFound,
        ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/platform/users/{userId}/tokens");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformDeleteUserTokens"));
        Summary(summary =>
        {
            summary.Summary = "Invalidates every access and refresh token for a user.";
            summary.Description = "Atomically increments the user's global token version.";
        });
    }

    public override async Task<Results<Ok<PlatformUserResponse>, NotFound,
        ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        if (actor.IsImpersonating)
            return TypedResults.Forbid();
        var user = await platform.InvalidateTokensAsync(
            Route<Guid>("userId"),
            actor.UserId,
            timeProvider.GetUtcNow(),
            ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlatformUserMapping.ToResponse(user));
    }
}

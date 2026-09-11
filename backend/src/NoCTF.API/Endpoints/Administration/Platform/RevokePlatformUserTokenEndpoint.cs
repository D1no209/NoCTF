using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class RevokePlatformUserTokenEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/platform/users/{userId}/tokens/{jwtId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformRevokeUserToken"));
        Summary(summary => summary.Summary =
            "Revokes one JWT previously issued by the calling administrator.");
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>>
        ExecuteAsync(CancellationToken ct)
    {
        if (actor.IsImpersonating)
            return TypedResults.Forbid();
        var result = await platform.RevokeIssuedTokenAsync(
            actor.UserId,
            Route<Guid>("userId"),
            Route<Guid>("jwtId"),
            timeProvider.GetUtcNow(),
            ct);
        return result == RevokeAdminIssuedAccessTokenState.Revoked
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}

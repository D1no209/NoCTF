using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class GetPlatformUserEndpoint(ManagePlatform platform)
    : EndpointWithoutRequest<Results<Ok<PlatformUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/platform/users/{userId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Summary(summary => summary.Summary = "Gets one platform user.");
    }

    public override async Task<Results<Ok<PlatformUserResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var user = await platform.GetUserAsync(Route<Guid>("userId"), ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlatformUserMapping.ToResponse(user));
    }
}

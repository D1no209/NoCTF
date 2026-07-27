using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class InvalidatePlatformUserTokensEndpoint(ManagePlatform platform)
    : EndpointWithoutRequest<Results<Ok<PlatformUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/platform/users/{userId}/tokens/invalidate");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformInvalidateUserTokens"));
        Summary(summary =>
        {
            summary.Summary = "Invalidates every access and refresh token for a user.";
            summary.Description = "Atomically increments the user's global token version.";
        });
    }

    public override async Task<Results<Ok<PlatformUserResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var user = await platform.InvalidateTokensAsync(
            Route<Guid>("userId"), DateTimeOffset.UtcNow, ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlatformUserMapping.ToResponse(user));
    }
}

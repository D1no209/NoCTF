using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Security;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformUserDetailResponse(
    PlatformUserResponse User,
    CurrentUserSchoolIdentityResponse SchoolIdentity);

public sealed class GetPlatformUserEndpoint(
    ManagePlatform platform,
    AccountPrivacy privacy,
    IUserContext actor)
    : EndpointWithoutRequest<Results<Ok<PlatformUserDetailResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/platform/users/{userId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetUser"));
        Summary(summary =>
        {
            summary.Summary = "Gets one platform user.";
            summary.Description = "Returns platform identity and token-version metadata to administrators only.";
        });
    }

    public override async Task<Results<Ok<PlatformUserDetailResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var userId = Route<Guid>("userId");
        var user = await platform.GetUserAsync(userId, ct);
        var privateDetails = await privacy.ReadPlatformAsync(actor.UserId, userId, ct);
        return user is null || privateDetails is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new PlatformUserDetailResponse(
                PlatformUserMapping.ToResponse(user),
                new CurrentUserSchoolIdentityResponse(
                    privateDetails.Identity.FullName,
                    privateDetails.Identity.StudentNumber)));
    }
}

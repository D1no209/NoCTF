using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class GetUserProfileRequest
{
    public Guid UserId { get; set; }
}

public sealed record PublicUserProfileResponse(
    Guid UserId,
    string UserName,
    string? Email,
    string? Description,
    string? AvatarUrl,
    bool IsEmailPublic);

public sealed class GetUserProfileEndpoint(
    GetPublicUserProfile getProfile,
    IUserContext user,
    LinkGenerator links)
    : Endpoint<GetUserProfileRequest, Results<Ok<PublicUserProfileResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/users/{userId}");
        AllowAnonymous();
        Description(builder => builder.WithName("UserProfile_Get"));
        Summary(summary => summary.Summary = "Returns a user's public profile.");
    }

    public override async Task<Results<Ok<PublicUserProfileResponse>, NotFound>> ExecuteAsync(
        GetUserProfileRequest request,
        CancellationToken ct)
    {
        var profile = await getProfile.ExecuteAsync(
            request.UserId,
            user.UserId,
            user.IsAdministrator,
            ct);
        if (profile is null)
            return TypedResults.NotFound();

        var avatarUrl = CurrentUserMapping.AvatarUrl(
            profile.Id,
            profile.AvatarObjectKey,
            links,
            HttpContext);
        return TypedResults.Ok(new PublicUserProfileResponse(
            profile.Id,
            profile.UserName,
            profile.Email,
            profile.Description,
            avatarUrl,
            profile.IsEmailPublic));
    }
}

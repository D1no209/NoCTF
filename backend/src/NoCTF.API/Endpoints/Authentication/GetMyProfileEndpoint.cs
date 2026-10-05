using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record CurrentUserSchoolIdentityResponse(
    string? FullName,
    string? StudentNumber);

public sealed record CurrentUserAppearanceResponse(bool WallpaperEnabled);

public sealed record CurrentUserPrivacyResponse(int IpRetentionDays);

public sealed record CurrentUserProfileResponse(
    string? Description,
    CurrentUserSchoolIdentityResponse SchoolIdentity,
    CurrentUserAppearanceResponse Appearance,
    CurrentUserPrivacyResponse Privacy);

internal static class CurrentUserProfileMapping
{
    public static CurrentUserProfileResponse ToResponse(
        UserProfile profile,
        AccountPrivacyOptions privacy) =>
        new(
            profile.Description,
            new(profile.SchoolFullName, profile.SchoolStudentNumber),
            new(profile.WallpaperEnabled),
            new(privacy.IpRetentionDays));

    public static CurrentUserProfileResponse ToResponse(
        NoCTF.Domain.Identity.User user,
        AccountPrivacyOptions privacy) =>
        new(
            user.Description,
            new(user.SchoolFullName, user.SchoolStudentNumber),
            new(user.WallpaperEnabled),
            new(privacy.IpRetentionDays));
}

public sealed class GetMyProfileEndpoint(
    GetCurrentUser getCurrent,
    IUserContext user,
    IOptions<AccountPrivacyOptions> privacy)
    : EndpointWithoutRequest<Results<Ok<CurrentUserProfileResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/auth/me/profile");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("Authentication_GetMyProfile"));
        Summary(summary => summary.Summary = "Gets the current user's editable profile.");
    }

    public override async Task<Results<Ok<CurrentUserProfileResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var profile = await getCurrent.ExecuteAsync(user.UserId, ct);
        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CurrentUserProfileMapping.ToResponse(profile, privacy.Value));
    }
}

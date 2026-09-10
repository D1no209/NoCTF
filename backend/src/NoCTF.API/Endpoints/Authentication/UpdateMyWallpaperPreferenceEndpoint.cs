using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record UpdateMyWallpaperPreferenceRequest(bool Enabled);

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<WallpaperPreferenceFailureCode>))]
public enum WallpaperPreferenceFailureCode
{
    WallpaperNotUploaded
}

public sealed record WallpaperPreferenceFailureResponse(WallpaperPreferenceFailureCode Code);

public sealed class UpdateMyWallpaperPreferenceEndpoint(
    UpdateCurrentUserWallpaperPreference update,
    IUserContext user,
    LinkGenerator links,
    TimeProvider timeProvider)
    : Endpoint<UpdateMyWallpaperPreferenceRequest,
        Results<Ok<CurrentUserResponse>, NotFound,
            BadRequest<WallpaperPreferenceFailureResponse>>>
{
    public override void Configure()
    {
        Put("/auth/me/wallpaper-preference");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("Authentication_UpdateMyWallpaperPreference"));
        Summary(summary => summary.Summary = "Enables or disables the current user's wallpaper.");
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound,
        BadRequest<WallpaperPreferenceFailureResponse>>> ExecuteAsync(
        UpdateMyWallpaperPreferenceRequest request,
        CancellationToken ct)
    {
        var result = await update.ExecuteAsync(
            user.UserId,
            request.Enabled,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            UserWallpaperPreferenceState.Updated =>
                TypedResults.Ok(CurrentUserMapping.ToResponse(
                    result.Profile!, links, HttpContext)),
            UserWallpaperPreferenceState.UserNotFound => TypedResults.NotFound(),
            UserWallpaperPreferenceState.WallpaperNotUploaded =>
                TypedResults.BadRequest(new WallpaperPreferenceFailureResponse(
                    WallpaperPreferenceFailureCode.WallpaperNotUploaded)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.State, null)
        };
    }
}

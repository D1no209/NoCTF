using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class GetMyWallpaperEndpoint(
    GetCurrentUserWallpaper getWallpaper,
    IUserContext user)
    : EndpointWithoutRequest<Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Get("/auth/me/wallpaper");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("Authentication_GetMyWallpaper"));
        Summary(summary => summary.Summary = "Returns the current user's uploaded wallpaper.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var wallpaper = await getWallpaper.ExecuteAsync(user.UserId, ct);
        return wallpaper is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(wallpaper.Content, wallpaper.ContentType);
    }
}

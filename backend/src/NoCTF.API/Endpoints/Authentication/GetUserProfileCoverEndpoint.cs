using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class GetUserProfileCoverRequest
{
    public Guid UserId { get; set; }
}

public sealed class GetUserProfileCoverEndpoint(GetPublicUserProfileCover getCover)
    : Endpoint<GetUserProfileCoverRequest, Results<FileStreamHttpResult, NotFound>>
{
    public override void Configure()
    {
        Options(builder => builder.WithMetadata(new NoCTF.Hosting.Observability.ApiRequestMetricsMetadata(
            NoCTF.Application.Observability.ApiRequestKind.Download)));
        Get("/users/{userId}/profile-cover");
        AllowAnonymous();
        Description(builder => builder.WithName("UserProfileCover_Get"));
        Summary(summary => summary.Summary = "Returns a user's public profile cover image.");
    }

    public override async Task<Results<FileStreamHttpResult, NotFound>> ExecuteAsync(
        GetUserProfileCoverRequest request,
        CancellationToken ct)
    {
        var cover = await getCover.ExecuteAsync(request.UserId, ct);
        if (cover is null)
            return TypedResults.NotFound();

        HttpContext.Response.Headers.CacheControl = "private,no-store";
        return TypedResults.Stream(cover.Content, cover.ContentType);
    }
}

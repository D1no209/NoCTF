using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record AdminIssuedAccessTokenResponse(
    Guid JwtId,
    Guid TargetUserId,
    string TargetUserName,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Reason);

public sealed record AdminIssuedAccessTokenListResponse(
    IReadOnlyList<AdminIssuedAccessTokenResponse> Items);

public sealed class ListPlatformUserTokensEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<AdminIssuedAccessTokenListResponse>,
        NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/platform/users/{userId}/tokens");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformListUserTokens"));
        Summary(summary => summary.Summary =
            "Lists the caller's active administrator-issued JWTs for one user.");
    }

    public override async Task<Results<Ok<AdminIssuedAccessTokenListResponse>,
        NotFound, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (actor.IsImpersonating)
            return TypedResults.Forbid();
        var userId = Route<Guid>("userId");
        if (await platform.GetUserAsync(userId, ct) is null)
            return TypedResults.NotFound();
        var tokens = await platform.ListIssuedTokensAsync(
            actor.UserId,
            userId,
            timeProvider.GetUtcNow(),
            ct);
        return TypedResults.Ok(new AdminIssuedAccessTokenListResponse(
            tokens.Select(token => new AdminIssuedAccessTokenResponse(
                token.JwtId,
                token.TargetUserId,
                token.TargetUserName,
                token.IssuedAt,
                token.ExpiresAt,
                token.Reason)).ToArray()));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string Email,
    UserRole Role,
    UserKind Kind,
    bool EmailVerified,
    string? Description,
    string? AvatarUrl,
    bool IsEmailPublic);

internal static class CurrentUserMapping
{
    public static CurrentUserResponse ToResponse(
        UserProfile profile,
        LinkGenerator links,
        HttpContext httpContext)
    {
        var avatarUrl = AvatarUrl(profile.Id, profile.AvatarObjectKey, links, httpContext);

        return new(
            profile.Id,
            profile.UserName,
            profile.Email,
            profile.Role,
            profile.Kind,
            profile.EmailVerified,
            profile.Description,
            avatarUrl,
            profile.IsEmailPublic);
    }

    public static string? AvatarUrl(
        Guid userId,
        string? avatarObjectKey,
        LinkGenerator links,
        HttpContext httpContext)
    {
        string? avatarUrl = null;
        if (!string.IsNullOrWhiteSpace(avatarObjectKey))
        {
            var path = links.GetPathByName(
                httpContext,
                "UserAvatar_Get",
                new { userId });
            if (path is not null)
            {
                var revision = Uri.EscapeDataString(Path.GetFileName(avatarObjectKey));
                avatarUrl = $"{path}?revision={revision}";
            }
        }

        return avatarUrl;
    }
}

public sealed class GetMeEndpoint(
    GetCurrentUser getCurrent,
    IUserContext user,
    LinkGenerator links)
    : EndpointWithoutRequest<Results<Ok<CurrentUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/auth/me");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Get the current user";
            summary.Description = "Returns the authenticated account profile.";
        });
    }

    public override async Task<Results<Ok<CurrentUserResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var profile = await getCurrent.ExecuteAsync(user.UserId, ct);
        return profile is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(CurrentUserMapping.ToResponse(profile, links, HttpContext));
    }
}

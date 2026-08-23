using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Authentication;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UserRoleProtocol>))]
public enum UserRoleProtocol
{
    User,
    Organizer,
    Administrator
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UserKindProtocol>))]
public enum UserKindProtocol
{
    Human,
    Bot
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UserAccountStatusProtocol>))]
public enum UserAccountStatusProtocol
{
    Active,
    Banned,
    Disabled,
    Anonymized
}

[Mapper]
public static partial class IdentityProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial UserRoleProtocol ToProtocol(UserRole value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial UserKindProtocol ToProtocol(UserKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial UserAccountStatusProtocol ToProtocol(UserAccountStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial UserRole ToDomain(UserRoleProtocol value);
}

public sealed record CurrentUserResponse(
    Guid UserId,
    string UserName,
    string Email,
    UserRoleProtocol Role,
    UserKindProtocol Kind,
    bool EmailVerified,
    string? Description,
    string? AvatarUrl);

internal static class CurrentUserMapping
{
    public static CurrentUserResponse ToResponse(
        UserProfile profile,
        LinkGenerator links,
        HttpContext httpContext)
    {
        var avatarUrl = AvatarUrl(profile.Id, profile.AvatarFileId, links, httpContext);

        return new(
            profile.Id,
            profile.UserName,
            profile.Email,
            IdentityProtocolMapper.ToProtocol(profile.Role),
            IdentityProtocolMapper.ToProtocol(profile.Kind),
            profile.EmailVerified,
            profile.Description,
            avatarUrl);
    }

    public static string? AvatarUrl(
        Guid userId,
        Guid? avatarFileId,
        LinkGenerator links,
        HttpContext httpContext)
    {
        string? avatarUrl = null;
        if (avatarFileId is not null)
        {
            var path = links.GetPathByName(
                httpContext,
                "UserAvatar_Get",
                new { userId });
            if (path is not null)
            {
                avatarUrl = $"{path}?revision={avatarFileId.Value:N}";
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

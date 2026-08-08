using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformUserResponse(
    Guid Id,
    string UserName,
    string Email,
    UserKindProtocol Kind,
    UserRoleProtocol Role,
    UserAccountStatusProtocol AccountStatus,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PlatformUserListResponse(IReadOnlyList<PlatformUserResponse> Items);

internal static class PlatformUserMapping
{
    public static PlatformUserResponse ToResponse(PlatformUserView view) =>
        new(
            view.Id, view.UserName, view.Email,
            IdentityProtocolMapper.ToProtocol(view.Kind),
            IdentityProtocolMapper.ToProtocol(view.Role),
            IdentityProtocolMapper.ToProtocol(view.AccountStatus),
            view.TokenVersion,
            view.EmailVerified, view.CreatedAt, view.UpdatedAt);
}

public sealed class ListPlatformUsersEndpoint(ManagePlatform platform)
    : EndpointWithoutRequest<Ok<PlatformUserListResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/users");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformListUsers"));
        Summary(summary =>
        {
            summary.Summary = "Lists platform users.";
            summary.Description = "Returns platform role and token-version metadata to administrators only.";
        });
    }

    public override async Task<Ok<PlatformUserListResponse>> ExecuteAsync(CancellationToken ct) =>
        TypedResults.Ok(new PlatformUserListResponse(
            (await platform.ListUsersAsync(ct)).Select(PlatformUserMapping.ToResponse).ToArray()));
}

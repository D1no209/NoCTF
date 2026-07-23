using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformUserResponse(
    Guid Id,
    string UserName,
    string Email,
    UserRole Role,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PlatformUserListResponse(IReadOnlyList<PlatformUserResponse> Items);

internal static class PlatformUserMapping
{
    public static PlatformUserResponse ToResponse(PlatformUserView view) =>
        new(
            view.Id, view.UserName, view.Email, view.Role, view.TokenVersion,
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
        Summary(summary => summary.Summary = "Lists platform users.");
    }

    public override async Task<Ok<PlatformUserListResponse>> ExecuteAsync(CancellationToken ct) =>
        TypedResults.Ok(new PlatformUserListResponse(
            (await platform.ListUsersAsync(ct)).Select(PlatformUserMapping.ToResponse).ToArray()));
}

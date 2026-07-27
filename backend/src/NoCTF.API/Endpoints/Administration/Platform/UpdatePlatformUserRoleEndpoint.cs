using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdatePlatformUserRoleRequest
{
    public UserRole Role { get; set; }
}

public sealed class UpdatePlatformUserRoleValidator : Validator<UpdatePlatformUserRoleRequest>
{
    public UpdatePlatformUserRoleValidator() =>
        RuleFor(request => request.Role).IsInEnum();
}

public sealed class UpdatePlatformUserRoleEndpoint(ManagePlatform platform)
    : Endpoint<UpdatePlatformUserRoleRequest, Results<Ok<PlatformUserResponse>, NotFound>>
{
    public override void Configure()
    {
        Put("/admin/platform/users/{userId}/role");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUpdateUserRole"));
        Summary(summary =>
        {
            summary.Summary = "Updates a platform role and invalidates existing tokens.";
            summary.Description = "Atomically changes the bounded user role and increments the token version.";
        });
    }

    public override async Task<Results<Ok<PlatformUserResponse>, NotFound>> ExecuteAsync(
        UpdatePlatformUserRoleRequest request,
        CancellationToken ct)
    {
        var user = await platform.UpdateRoleAsync(
            Route<Guid>("userId"), request.Role, DateTimeOffset.UtcNow, ct);
        return user is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(PlatformUserMapping.ToResponse(user));
    }
}

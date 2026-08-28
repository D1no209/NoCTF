using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdatePlatformUserRoleRequest
{
    public UserRoleProtocol Role { get; set; }
}

public sealed class UpdatePlatformUserRoleValidator : Validator<UpdatePlatformUserRoleRequest>
{
    public UpdatePlatformUserRoleValidator() =>
        RuleFor(request => request.Role).IsInEnum();
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<UpdatePlatformUserRoleConflictCode>))]
public enum UpdatePlatformUserRoleConflictCode
{
    ActiveOwnerOrManagerAssignments,
    LastAdministratorProtected
}

public sealed record UpdatePlatformUserRoleConflictResponse(
    UpdatePlatformUserRoleConflictCode Code,
    IReadOnlyList<Guid> CompetitionIds,
    IReadOnlyList<Guid> ChallengeIds);

public sealed class UpdatePlatformUserRoleEndpoint(ManagePlatform platform, TimeProvider timeProvider)
    : Endpoint<UpdatePlatformUserRoleRequest,
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserRoleConflictResponse>,
            ProblemHttpResult>>
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
            summary.Description =
                "Atomically changes the bounded user role and increments the token version unless active owner or manager assignments block a downgrade.";
        });
    }

    public override async Task<
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserRoleConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        UpdatePlatformUserRoleRequest request,
        CancellationToken ct)
    {
        var userId = Route<Guid>("userId");
        var result = await platform.UpdateRoleAsync(
            userId,
            IdentityProtocolMapper.ToDomain(request.Role),
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            UpdatePlatformRoleState.Updated =>
                TypedResults.Ok(PlatformUserMapping.ToResponse(result.User!)),
            UpdatePlatformRoleState.UserNotFound => TypedResults.NotFound(),
            UpdatePlatformRoleState.ActiveOwnerOrManagerAssignments =>
                TypedResults.Conflict(new UpdatePlatformUserRoleConflictResponse(
                    UpdatePlatformUserRoleConflictCode.ActiveOwnerOrManagerAssignments,
                    result.Blockers!.CompetitionIds,
                    result.Blockers.ChallengeIds)),
            UpdatePlatformRoleState.LastAdministratorProtected =>
                TypedResults.Conflict(new UpdatePlatformUserRoleConflictResponse(
                    UpdatePlatformUserRoleConflictCode.LastAdministratorProtected,
                    [],
                    [])),
            UpdatePlatformRoleState.InvalidBotRole => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Role is not valid for a bot.",
                detail: "Bot identities may use User or Organizer, but never Administrator."),
            _ => throw new InvalidOperationException(
                $"Unsupported platform role update state: {result.State}.")
        };
    }
}

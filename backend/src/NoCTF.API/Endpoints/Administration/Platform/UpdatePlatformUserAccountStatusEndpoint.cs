using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PlatformManagedUserAccountStatusProtocol>))]
public enum PlatformManagedUserAccountStatusProtocol
{
    Active,
    Banned,
    Disabled
}

public sealed class UpdatePlatformUserAccountStatusRequest
{
    public PlatformManagedUserAccountStatusProtocol? AccountStatus { get; set; }
}

public sealed class UpdatePlatformUserAccountStatusValidator
    : Validator<UpdatePlatformUserAccountStatusRequest>
{
    public UpdatePlatformUserAccountStatusValidator() =>
        RuleFor(request => request.AccountStatus).NotNull().IsInEnum();
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UpdatePlatformUserAccountStatusConflictCode>))]
public enum UpdatePlatformUserAccountStatusConflictCode
{
    AnonymizedAccountImmutable,
    LastAdministratorProtected
}

public sealed record UpdatePlatformUserAccountStatusConflictResponse(
    UpdatePlatformUserAccountStatusConflictCode Code);

public sealed class UpdatePlatformUserAccountStatusEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : Endpoint<UpdatePlatformUserAccountStatusRequest,
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserAccountStatusConflictResponse>>>
{
    public override void Configure()
    {
        Put("/admin/platform/users/{userId}/account-status");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformUpdateUserAccountStatus"));
        Summary(summary =>
        {
            summary.Summary = "Updates a platform user's active account status.";
            summary.Description =
                "Activates, bans, or disables an account, invalidates existing tokens, and preserves at least one active human administrator.";
        });
    }

    public override async Task<
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserAccountStatusConflictResponse>>> ExecuteAsync(
        UpdatePlatformUserAccountStatusRequest request,
        CancellationToken ct)
    {
        var result = await platform.UpdateAccountStatusAsync(
            Route<Guid>("userId"),
            actor.UserId,
            ToDomain(request.AccountStatus!.Value),
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            UpdatePlatformUserStatusState.Updated =>
                TypedResults.Ok(PlatformUserMapping.ToResponse(result.User!)),
            UpdatePlatformUserStatusState.UserNotFound => TypedResults.NotFound(),
            UpdatePlatformUserStatusState.AnonymizedAccountImmutable =>
                TypedResults.Conflict(new UpdatePlatformUserAccountStatusConflictResponse(
                    UpdatePlatformUserAccountStatusConflictCode.AnonymizedAccountImmutable)),
            UpdatePlatformUserStatusState.LastAdministratorProtected =>
                TypedResults.Conflict(new UpdatePlatformUserAccountStatusConflictResponse(
                    UpdatePlatformUserAccountStatusConflictCode.LastAdministratorProtected)),
            _ => throw new InvalidOperationException(
                $"Unsupported platform account status update state: {result.State}.")
        };
    }

    private static UserAccountStatus ToDomain(
        PlatformManagedUserAccountStatusProtocol status) => status switch
        {
            PlatformManagedUserAccountStatusProtocol.Active => UserAccountStatus.Active,
            PlatformManagedUserAccountStatusProtocol.Banned => UserAccountStatus.Banned,
            PlatformManagedUserAccountStatusProtocol.Disabled => UserAccountStatus.Disabled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}

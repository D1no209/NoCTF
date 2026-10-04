using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Administration.UserAccounts;

namespace NoCTF.API.Endpoints.Administration.Platform;

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<PlatformUserDeletionMode>))]
public enum PlatformUserDeletionMode
{
    HardDelete,
    Anonymize
}

public sealed class DeletePlatformUserRequest
{
    public PlatformUserDeletionMode? Mode { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class DeletePlatformUserValidator : Validator<DeletePlatformUserRequest>
{
    public DeletePlatformUserValidator()
    {
        RuleFor(request => request.Mode).NotNull().IsInEnum();
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(ManageUserAccounts.MaximumReasonLength);
    }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<PlatformUserDeletionOutcomeCode>))]
public enum PlatformUserDeletionOutcomeCode
{
    PhysicallyDeleted,
    Anonymized
}

public sealed record PlatformUserDeletionResponse(PlatformUserDeletionOutcomeCode Outcome);

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<PlatformUserDeletionConflictCode>))]
public enum PlatformUserDeletionConflictCode
{
    HardDeleteBlocked,
    SelfDeletionForbidden,
    LastAdministratorProtected,
    AlreadyAnonymized
}

public sealed record PlatformUserDeletionConflictResponse(
    PlatformUserDeletionConflictCode Code,
    string Detail,
    PlatformUserDeletionPreviewResponse? Preview)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class DeletePlatformUserEndpoint(
    ManageUserAccounts accounts,
    IUserContext actor,
    TimeProvider timeProvider)
    : Endpoint<DeletePlatformUserRequest,
        Results<
            Ok<PlatformUserDeletionResponse>,
            NotFound,
            Conflict<PlatformUserDeletionConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/platform/users/{userId}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformDeleteUser"));
        Summary(summary =>
        {
            summary.Summary = "Deletes or irreversibly anonymizes a platform user.";
            summary.Description =
                "Physical deletion is allowed only without business references. Anonymization preserves all historical records and never cascades them.";
        });
    }

    public override async Task<
        Results<
            Ok<PlatformUserDeletionResponse>,
            NotFound,
            Conflict<PlatformUserDeletionConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        DeletePlatformUserRequest request,
        CancellationToken ct)
    {
        var result = await accounts.DeleteAsync(
            Route<Guid>("userId"),
            actor.UserId,
            request.Mode == PlatformUserDeletionMode.HardDelete
                ? UserDeletionMode.HardDelete
                : UserDeletionMode.Anonymize,
            request.Reason,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            UserDeletionState.PhysicallyDeleted =>
                TypedResults.Ok(new PlatformUserDeletionResponse(
                    PlatformUserDeletionOutcomeCode.PhysicallyDeleted)),
            UserDeletionState.Anonymized =>
                TypedResults.Ok(new PlatformUserDeletionResponse(
                    PlatformUserDeletionOutcomeCode.Anonymized)),
            UserDeletionState.UserNotFound => TypedResults.NotFound(),
            UserDeletionState.HardDeleteBlocked => Conflict(
                PlatformUserDeletionConflictCode.HardDeleteBlocked,
                result.Preview),
            UserDeletionState.SelfDeletionForbidden => Conflict(
                PlatformUserDeletionConflictCode.SelfDeletionForbidden,
                result.Preview),
            UserDeletionState.LastAdministratorProtected => Conflict(
                PlatformUserDeletionConflictCode.LastAdministratorProtected,
                result.Preview),
            UserDeletionState.AlreadyAnonymized => Conflict(
                PlatformUserDeletionConflictCode.AlreadyAnonymized,
                result.Preview),
            UserDeletionState.ReasonInvalid => ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.DeletePlatformUserTitleDeletionReasonInvalid),
                detail: ApiMessages.Get(ApiMessageId.DeletePlatformUserDetailReasonBetweenCharactersRequired)),
            _ => throw new InvalidOperationException(
                $"Unsupported platform user deletion state: {result.State}.")
        };
    }

    private static Conflict<PlatformUserDeletionConflictResponse> Conflict(
        PlatformUserDeletionConflictCode code,
        UserDeletionPreview? preview) =>
        TypedResults.Conflict(new PlatformUserDeletionConflictResponse(
            code,
            code switch
            {
                PlatformUserDeletionConflictCode.HardDeleteBlocked =>
                    "This user still owns or is referenced by business records. Review the preview and anonymize the account instead.",
                PlatformUserDeletionConflictCode.SelfDeletionForbidden =>
                    "Administrators cannot delete or anonymize their own account.",
                PlatformUserDeletionConflictCode.LastAdministratorProtected =>
                    "This account is the last active human administrator and cannot be removed.",
                PlatformUserDeletionConflictCode.AlreadyAnonymized =>
                    "This account has already been anonymized and cannot be changed again.",
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
            },
            preview is null ? null : PlatformUserDeletionMapping.ToResponse(preview)));
}

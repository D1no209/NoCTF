using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdatePlatformUserEmailVerificationRequest
{
    public bool EmailVerified { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<UpdatePlatformUserEmailVerificationConflictCode>))]
public enum UpdatePlatformUserEmailVerificationConflictCode
{
    AnonymizedAccountImmutable
}

public sealed record UpdatePlatformUserEmailVerificationConflictResponse(
    UpdatePlatformUserEmailVerificationConflictCode Code);

public sealed class UpdatePlatformUserEmailVerificationEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : Endpoint<UpdatePlatformUserEmailVerificationRequest,
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserEmailVerificationConflictResponse>>>
{
    public override void Configure()
    {
        Put("/admin/platform/users/{userId}/email-verification");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName(
            "AdminPlatformUpdateUserEmailVerification"));
        Summary(summary =>
        {
            summary.Summary = "Updates a platform user's email verification status.";
            summary.Description =
                "Marks an email as verified or unverified and invalidates the user's existing tokens when the status changes.";
        });
    }

    public override async Task<
        Results<
            Ok<PlatformUserResponse>,
            NotFound,
            Conflict<UpdatePlatformUserEmailVerificationConflictResponse>>> ExecuteAsync(
        UpdatePlatformUserEmailVerificationRequest request,
        CancellationToken ct)
    {
        var result = await platform.UpdateEmailVerificationAsync(
            Route<Guid>("userId"),
            actor.UserId,
            request.EmailVerified,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            UpdatePlatformUserEmailVerificationState.Updated =>
                TypedResults.Ok(PlatformUserMapping.ToResponse(result.User!)),
            UpdatePlatformUserEmailVerificationState.UserNotFound =>
                TypedResults.NotFound(),
            UpdatePlatformUserEmailVerificationState.AnonymizedAccountImmutable =>
                TypedResults.Conflict(
                    new UpdatePlatformUserEmailVerificationConflictResponse(
                        UpdatePlatformUserEmailVerificationConflictCode
                            .AnonymizedAccountImmutable)),
            _ => throw new InvalidOperationException(
                $"Unsupported platform email verification update state: {result.State}.")
        };
    }
}

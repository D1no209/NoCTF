using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class IssuePlatformUserTokenRequest
{
    public required long ExpiresInSeconds { get; set; }
    public required string Reason { get; set; }
}

public sealed class IssuePlatformUserTokenValidator
    : Validator<IssuePlatformUserTokenRequest>
{
    public IssuePlatformUserTokenValidator()
    {
        RuleFor(request => request.ExpiresInSeconds).InclusiveBetween(
            ManagePlatform.MinimumIssuedTokenLifetimeSeconds,
            ManagePlatform.MaximumIssuedTokenLifetimeSeconds);
        RuleFor(request => request.Reason)
            .NotEmpty()
            .Must(reason => !string.IsNullOrWhiteSpace(reason)
                && reason.Trim().Length is >= ManagePlatform.MinimumTokenIssuanceReasonLength
                    and <= ManagePlatform.MaximumTokenIssuanceReasonLength);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<IssuePlatformUserTokenFailureCode>))]
public enum IssuePlatformUserTokenFailureCode
{
    AccountInactive
}

public sealed record IssuePlatformUserTokenFailureResponse(
    IssuePlatformUserTokenFailureCode Code,
    string Message);

public sealed record IssuePlatformUserTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid JwtId,
    Guid TargetUserId,
    string TargetUserName);

public sealed class IssuePlatformUserTokenEndpoint(
    ManagePlatform platform,
    IUserContext actor,
    TimeProvider timeProvider)
    : Endpoint<IssuePlatformUserTokenRequest,
        Results<Ok<IssuePlatformUserTokenResponse>, NotFound, ForbidHttpResult,
            Conflict<IssuePlatformUserTokenFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/users/{userId}/tokens");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformIssueUserToken"));
        Summary(summary =>
        {
            summary.Summary = "Issues a bounded-lifetime Access JWT for an active user.";
            summary.Description =
                "The token uses the target identity and records the issuing administrator without creating a Refresh Token.";
        });
    }

    public override async Task<Results<Ok<IssuePlatformUserTokenResponse>, NotFound,
        ForbidHttpResult, Conflict<IssuePlatformUserTokenFailureResponse>,
        ProblemHttpResult>> ExecuteAsync(
        IssuePlatformUserTokenRequest request,
        CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (actor.IsImpersonating)
            return TypedResults.Forbid();
        var result = await platform.IssueUserTokenAsync(
            Route<Guid>("userId"),
            actor.UserId,
            request.ExpiresInSeconds,
            request.Reason,
            timeProvider.GetUtcNow(),
            ct);
        return result.Failure switch
        {
            IssuePlatformUserTokenFailure.UserNotFound => TypedResults.NotFound(),
            IssuePlatformUserTokenFailure.AccountInactive => TypedResults.Conflict(
                new IssuePlatformUserTokenFailureResponse(
                    IssuePlatformUserTokenFailureCode.AccountInactive,
                    "Only active accounts can receive administrator-issued tokens.")),
            IssuePlatformUserTokenFailure.InvalidLifetime
                or IssuePlatformUserTokenFailure.ReasonInvalid => TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Token was not issued."),
            _ => TypedResults.Ok(new IssuePlatformUserTokenResponse(
                result.Token!.Token,
                result.Token.ExpiresAt,
                result.Token.JwtId,
                result.TargetUser!.Id,
                result.TargetUser.UserName))
        };
    }
}

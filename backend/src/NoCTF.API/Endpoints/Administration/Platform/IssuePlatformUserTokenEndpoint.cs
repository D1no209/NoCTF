using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Administration;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class IssuePlatformUserTokenRequest
{
    public required long ExpiresInSeconds { get; set; }
}

public sealed class IssuePlatformUserTokenValidator
    : Validator<IssuePlatformUserTokenRequest>
{
    public IssuePlatformUserTokenValidator()
    {
        RuleFor(request => request.ExpiresInSeconds).InclusiveBetween(
            ManagePlatform.MinimumIssuedTokenLifetimeSeconds,
            ManagePlatform.MaximumIssuedTokenLifetimeSeconds);
    }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<IssuePlatformUserTokenFailureCode>))]
public enum IssuePlatformUserTokenFailureCode
{
    AccountInactive
}

public sealed record IssuePlatformUserTokenFailureResponse(
    IssuePlatformUserTokenFailureCode Code,
    string Message)
{
    public string Message { get; init; } = ApiMessages.Localize(Code, Message, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed record IssuePlatformUserTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid TargetUserId,
    string TargetUserName);

public sealed class IssuePlatformUserTokenEndpoint(
    ManagePlatform platform,
    TimeProvider timeProvider)
    : Endpoint<IssuePlatformUserTokenRequest,
        Results<Ok<IssuePlatformUserTokenResponse>, NotFound,
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
                "The token is an ordinary target-identity Access JWT without a Refresh Token or server-side session record.";
        });
    }

    public override async Task<Results<Ok<IssuePlatformUserTokenResponse>, NotFound,
        Conflict<IssuePlatformUserTokenFailureResponse>,
        ProblemHttpResult>> ExecuteAsync(
        IssuePlatformUserTokenRequest request,
        CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await platform.IssueUserTokenAsync(
            Route<Guid>("userId"),
            request.ExpiresInSeconds,
            timeProvider.GetUtcNow(),
            ct);
        return result.Failure switch
        {
            IssuePlatformUserTokenFailure.UserNotFound => TypedResults.NotFound(),
            IssuePlatformUserTokenFailure.AccountInactive => TypedResults.Conflict(
                new IssuePlatformUserTokenFailureResponse(
                    IssuePlatformUserTokenFailureCode.AccountInactive,
                    "Only active accounts can receive administrator-issued tokens.")),
            IssuePlatformUserTokenFailure.InvalidLifetime => ApiProblems.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: ApiMessages.Get(ApiMessageId.IssuePlatformUserTokenTitleTokenWasIssued)),
            _ => TypedResults.Ok(new IssuePlatformUserTokenResponse(
                result.Token!.Token,
                result.Token.ExpiresAt,
                result.TargetUser!.Id,
                result.TargetUser.UserName))
        };
    }
}

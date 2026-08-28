using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class IssuePlatformBotTokenRequest
{
    public long ExpiresInSeconds { get; set; }
}

public sealed record IssuePlatformBotTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class IssuePlatformBotTokenValidator : Validator<IssuePlatformBotTokenRequest>
{
    public IssuePlatformBotTokenValidator() =>
        RuleFor(request => request.ExpiresInSeconds).InclusiveBetween(
            ManagePlatform.MinimumBotTokenLifetimeSeconds,
            ManagePlatform.MaximumBotTokenLifetimeSeconds);
}

public sealed class IssuePlatformBotTokenEndpoint(ManagePlatform platform, TimeProvider timeProvider)
    : Endpoint<IssuePlatformBotTokenRequest,
        Results<Ok<IssuePlatformBotTokenResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/bots/{userId}/tokens");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformIssueBotToken"));
        Summary(summary =>
        {
            summary.Summary = "Issues a bounded-lifetime Access JWT for a Bot.";
            summary.Description =
                "Uses the ordinary Access scheme and is revoked by the Bot's TokenVersion.";
        });
    }

    public override async Task<
        Results<Ok<IssuePlatformBotTokenResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        IssuePlatformBotTokenRequest request,
        CancellationToken ct)
    {
        var result = await platform.IssueBotTokenAsync(
            Route<Guid>("userId"),
            request.ExpiresInSeconds,
            timeProvider.GetUtcNow(),
            ct);
        return result.Failure switch
        {
            IssueBotTokenFailure.UserNotFound => TypedResults.NotFound(),
            IssueBotTokenFailure.UserIsNotBot => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Token was not issued.",
                detail: "The selected user is not a bot."),
            IssueBotTokenFailure.UserInactive => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Token was not issued.",
                detail: "The selected bot account is not active."),
            IssueBotTokenFailure.InvalidLifetime => TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Token was not issued.",
                detail: "ExpiresInSeconds is outside the supported date range."),
            _ => TypedResults.Ok(new IssuePlatformBotTokenResponse(
                result.Token!.Token,
                result.Token.ExpiresAt))
        };
    }
}

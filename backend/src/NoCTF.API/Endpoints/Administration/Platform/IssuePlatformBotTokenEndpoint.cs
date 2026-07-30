using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.Bots;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class IssuePlatformBotTokenRequest
{
    public int ExpiresInSeconds { get; set; }
}

public sealed record PlatformBotAccessTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed class IssuePlatformBotTokenValidator : Validator<IssuePlatformBotTokenRequest>
{
    public IssuePlatformBotTokenValidator() =>
        RuleFor(request => request.ExpiresInSeconds)
            .InclusiveBetween(
                IssuePlatformBotToken.MinimumLifetimeSeconds,
                IssuePlatformBotToken.MaximumLifetimeSeconds);
}

public sealed class IssuePlatformBotTokenEndpoint(IssuePlatformBotToken issue)
    : Endpoint<IssuePlatformBotTokenRequest,
        Results<Ok<PlatformBotAccessTokenResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/bots/{botUserId}/tokens");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformIssueBotToken"));
        Summary(summary =>
        {
            summary.Summary = "Issues a bounded-lifetime Access JWT for a Bot.";
            summary.Description =
                "The JWT uses the ordinary Access scheme and is revoked by the Bot's TokenVersion.";
        });
    }

    public override async Task<
        Results<Ok<PlatformBotAccessTokenResponse>, NotFound, ProblemHttpResult>>
        ExecuteAsync(IssuePlatformBotTokenRequest request, CancellationToken ct)
    {
        var result = await issue.ExecuteAsync(
            Route<Guid>("botUserId"),
            request.ExpiresInSeconds,
            ct);
        if (result.ErrorCode == "bot_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bot token was not issued.",
                detail: result.ErrorMessage);
        }
        return TypedResults.Ok(new PlatformBotAccessTokenResponse(
            result.Value!.Token,
            result.Value.ExpiresAt));
    }
}

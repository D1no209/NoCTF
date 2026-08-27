using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed record GetGameplayFactValueResponse(
    Guid GameplayFactId,
    GameplayFactKindProtocol Kind,
    string Value);

public sealed class GetGameplayFactValueEndpoint(
    ReadPlayerGameplayFactValue read,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<GetGameplayFactValueResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/value");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetGameplayFactValueEndpoint"));
        Summary(summary =>
        {
            summary.Summary = "Reads one submitted Flag value owned by the current team.";
            summary.Description =
                "Only FlagAttempt and BreakAttempt values from the authenticated user's team are available.";
        });
    }

    public override async Task<Results<Ok<GetGameplayFactValueResponse>, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var value = await read.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("gameplayFactId"),
            user.UserId,
            cancellationToken);
        if (value is null)
            return TypedResults.NotFound();

        HttpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new GetGameplayFactValueResponse(
            value.GameplayFactId,
            GameplayFactMapper.ToProtocol(value.Kind),
            value.Value));
    }
}

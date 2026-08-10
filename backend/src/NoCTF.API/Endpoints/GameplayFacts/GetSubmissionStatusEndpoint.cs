using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Status;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class GetGameplayFactStatusRequest
{
    public Guid CompetitionId { get; set; }
    public Guid GameplayFactId { get; set; }
}

public sealed class GetGameplayFactStatusEndpoint(
    IGameplayFactStatusReader statusReader,
    IUserContext userContext) : Endpoint<GetGameplayFactStatusRequest, Results<Ok<GameplayFactStatusResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/gameplay-facts/{gameplayFactId}");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<GameplayFactStatusResponse>, NotFound>> ExecuteAsync(
        GetGameplayFactStatusRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.GameplayFactId = Route<Guid>("gameplayFactId");
        var result = await statusReader.FindAsync(
            request.CompetitionId,
            request.GameplayFactId,
            userContext.UserId,
            cancellationToken);
        if (result is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(GameplayFactMapper.ToStatusResponse(result));
    }
}

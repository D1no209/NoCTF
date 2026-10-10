using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloRuntimeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed class GetLiveSoloRuntimeEndpoint(ILiveSoloRuntimeStore store, IUserContext user, TimeProvider clock)
    : Endpoint<GetLiveSoloRuntimeRequest, Results<Ok<RuntimeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/runtime");
        AuthSchemes("Bearer"); Description(x => x.WithName("GetLiveSoloRuntime"));
        Summary(x => x.Summary = "Reads the current team's opened execution environment and its verified proxy accesses.");
    }
    public override async Task<Results<Ok<RuntimeResponse>, NotFound>> ExecuteAsync(GetLiveSoloRuntimeRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.ReadAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, clock.GetUtcNow()), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(RuntimeEndpointMapping.ToResponse(result, HttpContext.Request));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DrawLiveSoloDirectionRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } }
public sealed class DrawLiveSoloDirectionEndpoint(ILiveSoloDirectionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<DrawLiveSoloDirectionRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/direction-pool/draw"); AuthSchemes("Bearer");
        Description(x => x.WithName("DrawLiveSoloDirection"));
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(DrawLiveSoloDirectionRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await store.DrawAsync(req.CompetitionId, req.MatchId, user.UserId, clock.GetUtcNow(), ct));
}

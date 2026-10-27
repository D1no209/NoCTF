using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DeleteLiveSoloMatchRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } }
public sealed record DeleteLiveSoloMatchResponse(IReadOnlyList<Guid> MatchIds);
public sealed class DeleteLiveSoloMatchValidator : Validator<DeleteLiveSoloMatchRequest>
{
    public DeleteLiveSoloMatchValidator() { RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); }
}
public sealed class DeleteLiveSoloMatchEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<DeleteLiveSoloMatchRequest, Results<Ok<DeleteLiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Delete("/competitions/{competitionId}/live-solo/matches/{matchId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("DeleteLiveSoloMatch"));
    }
    public override async Task<Results<Ok<DeleteLiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>> ExecuteAsync(DeleteLiveSoloMatchRequest req, CancellationToken ct)
    {
        var result = await matches.DeleteAsync(new(req.CompetitionId, req.MatchId, user.UserId, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(new DeleteLiveSoloMatchResponse(result.MatchIds)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure.Value))
        };
    }
}

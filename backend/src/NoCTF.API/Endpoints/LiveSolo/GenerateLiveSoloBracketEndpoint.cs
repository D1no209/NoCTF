using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Brackets;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GenerateLiveSoloBracketRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public IReadOnlyList<Guid> TeamIds { get; set; } = [];
}
public sealed class GenerateLiveSoloBracketValidator : Validator<GenerateLiveSoloBracketRequest>
{
    public GenerateLiveSoloBracketValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
        RuleFor(x => x.TeamIds).NotEmpty().Must(x => x.Count is >= 2 and <= 1024); RuleForEach(x => x.TeamIds).NotEmpty();
    }
}
public sealed class GenerateLiveSoloBracketEndpoint(ManageLiveSoloBracket brackets, IUserContext user, TimeProvider clock)
    : Endpoint<GenerateLiveSoloBracketRequest, Results<Ok<LiveSoloBracketResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/bracket"); AuthSchemes("Bearer");
        Description(x => x.WithName("GenerateLiveSoloBracket"));
        Summary(x => x.Summary = "Generates a seeded single/double elimination bracket before Matches exist; byes do not count as defeats.");
    }
    public override async Task<Results<Ok<LiveSoloBracketResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(GenerateLiveSoloBracketRequest req, CancellationToken ct)
    {
        var result = await brackets.GenerateAsync(new(req.CompetitionId, user.UserId, req.ExpectedStamp, req.TeamIds, clock.GetUtcNow()), ct);
        return result.Failure switch
        {
            null when result.Bracket is not null => TypedResults.Ok(LiveSoloBracketProtocol.Map(result.Bracket)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}

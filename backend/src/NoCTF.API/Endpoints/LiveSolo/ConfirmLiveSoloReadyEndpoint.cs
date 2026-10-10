using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ConfirmLiveSoloReadyRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid ExpectedStamp { get; set; }
}
public sealed class ConfirmLiveSoloReadyValidator : Validator<ConfirmLiveSoloReadyRequest>
{
    public ConfirmLiveSoloReadyValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
    }
}
public sealed class ConfirmLiveSoloReadyEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<ConfirmLiveSoloReadyRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/ready"); AuthSchemes("Bearer");
        Description(x => x.WithName("ConfirmLiveSoloReady"));
        Summary(x => x.Summary = "Confirms a locked roster member's team is ready for the current preparation.");
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(ConfirmLiveSoloReadyRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await matches.ReadyAsync(req.CompetitionId, req.MatchId, user.UserId, req.ExpectedStamp, clock.GetUtcNow(), ct));
}

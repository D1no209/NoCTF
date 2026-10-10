using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class StartLiveSoloCountdownRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid ExpectedStamp { get; set; }
}
public sealed class StartLiveSoloCountdownValidator : Validator<StartLiveSoloCountdownRequest>
{
    public StartLiveSoloCountdownValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.RoundId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
    }
}
public sealed class StartLiveSoloCountdownEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<StartLiveSoloCountdownRequest, Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/countdown"); AuthSchemes("Bearer");
        Description(x => x.WithName("StartLiveSoloCountdown"));
        Summary(x => x.Summary = "Starts a judge-controlled countdown only after roster screens, resources and capacity are ready.");
    }
    public override async Task<Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(StartLiveSoloCountdownRequest req, CancellationToken ct) =>
        LiveSoloProtocol.RoundMutation(await matches.CountdownAsync(new(req.CompetitionId, req.MatchId, req.RoundId, user.UserId, req.ExpectedStamp, clock.GetUtcNow()), ct));
}

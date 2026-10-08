using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloRoundRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid? QuestionGroupId { get; set; }
}
public sealed class PrepareLiveSoloRoundValidator : Validator<PrepareLiveSoloRoundRequest>
{
    public PrepareLiveSoloRoundValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
    }
}
public sealed class PrepareLiveSoloRoundEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<PrepareLiveSoloRoundRequest, Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloRound"));
        Summary(x => x.Summary = "Prepares a fresh Round and scoped resources from a suitable unexposed question group.");
    }
    public override async Task<Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(PrepareLiveSoloRoundRequest req, CancellationToken ct) =>
        LiveSoloProtocol.RoundMutation(await matches.PrepareAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.ExpectedStamp, req.QuestionGroupId, clock.GetUtcNow()), ct));
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class RetryLiveSoloPreparationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid? QuestionId { get; set; }
}
public sealed class RetryLiveSoloPreparationValidator : Validator<RetryLiveSoloPreparationRequest>
{
    public RetryLiveSoloPreparationValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
        RuleFor(x => x.QuestionId).NotEqual(Guid.Empty).When(x => x.QuestionId != null);
    }
}
public sealed class RetryLiveSoloPreparationEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<RetryLiveSoloPreparationRequest, Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/preparation"); AuthSchemes("Bearer");
        Description(x => x.WithName("RetryLiveSoloPreparation"));
    }
    public override async Task<Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(RetryLiveSoloPreparationRequest req, CancellationToken ct) =>
        LiveSoloProtocol.RoundMutation(await matches.RetryPreparationAsync(new(req.CompetitionId, req.MatchId, req.RoundId, user.UserId, req.ExpectedStamp, clock.GetUtcNow(), req.QuestionId), ct));
}

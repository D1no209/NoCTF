using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SpecifyPreparedLiveSoloGroupRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionGroupId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public Guid ExpectedGroupStamp { get; set; }
    public Guid SelectionRevision { get; set; }
}
public sealed class SpecifyPreparedLiveSoloGroupValidator : Validator<SpecifyPreparedLiveSoloGroupRequest>
{
    public SpecifyPreparedLiveSoloGroupValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.RoundId).NotEmpty();
        RuleFor(x => x.QuestionGroupId).NotEmpty(); RuleFor(x => x.ExpectedStamp).NotEmpty();
        RuleFor(x => x.ExpectedGroupStamp).NotEmpty(); RuleFor(x => x.SelectionRevision).NotEmpty();
    }
}
public sealed class SpecifyPreparedLiveSoloGroupEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<SpecifyPreparedLiveSoloGroupRequest, Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/selection"); AuthSchemes("Bearer");
        Description(x => x.WithName("SpecifyPreparedLiveSoloGroup"));
    }
    public override async Task<Results<Ok<LiveSoloRoundResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(SpecifyPreparedLiveSoloGroupRequest req, CancellationToken ct) =>
        LiveSoloProtocol.RoundMutation(await matches.SpecifyPreparedGroupAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionGroupId,
            user.UserId, req.ExpectedStamp, req.ExpectedGroupStamp, req.SelectionRevision, clock.GetUtcNow()), ct));
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SetLiveSoloQuestionVisibilityRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public bool TeammatesMayViewQuestions { get; set; }
}
public sealed class SetLiveSoloQuestionVisibilityValidator : Validator<SetLiveSoloQuestionVisibilityRequest>
{
    public SetLiveSoloQuestionVisibilityValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.MatchId).NotEmpty();
    }
}
public sealed class SetLiveSoloQuestionVisibilityEndpoint(ManageLiveSoloMatches matches, IUserContext user)
    : Endpoint<SetLiveSoloQuestionVisibilityRequest, Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/live-solo/matches/{matchId}/question-visibility");
        AuthSchemes("Bearer");
        Description(x => x.WithName("SetLiveSoloQuestionVisibility"));
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(SetLiveSoloQuestionVisibilityRequest req, CancellationToken ct) =>
        await matches.SetQuestionVisibilityAsync(req.CompetitionId, req.MatchId, user.UserId, req.TeammatesMayViewQuestions, ct) switch
        {
            null => TypedResults.NoContent(),
            LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            _ => TypedResults.NotFound()
        };
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class CreateLiveSoloMatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid LeftTeamId { get; set; }
    public Guid RightTeamId { get; set; }
    public int? RequiredWins { get; set; }
}
public sealed class CreateLiveSoloMatchValidator : Validator<CreateLiveSoloMatchRequest>
{
    public CreateLiveSoloMatchValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.LeftTeamId).NotEmpty(); RuleFor(x => x.RightTeamId).NotEmpty();
        RuleFor(x => x.RequiredWins).InclusiveBetween(1, 1024).When(x => x.RequiredWins.HasValue);
    }
}
public sealed class CreateLiveSoloMatchEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<CreateLiveSoloMatchRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches"); AuthSchemes("Bearer");
        Description(x => x.WithName("CreateLiveSoloMatch"));
        Summary(x => x.Summary = "Creates a staff-managed direct Match with exclusive active team slots.");
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(CreateLiveSoloMatchRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await matches.CreateAsync(new(req.CompetitionId, user.UserId, req.LeftTeamId, req.RightTeamId, req.RequiredWins, clock.GetUtcNow()), ct));
}

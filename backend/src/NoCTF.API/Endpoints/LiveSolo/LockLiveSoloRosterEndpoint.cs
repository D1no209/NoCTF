using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class LockLiveSoloRosterRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ExpectedStamp { get; set; }
    public IReadOnlyList<Guid> UserIds { get; set; } = [];
}
public sealed class LockLiveSoloRosterValidator : Validator<LockLiveSoloRosterRequest>
{
    public LockLiveSoloRosterValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.ExpectedStamp).NotEmpty(); RuleFor(x => x.UserIds).NotEmpty(); RuleForEach(x => x.UserIds).NotEmpty();
    }
}
public sealed class LockLiveSoloRosterEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<LockLiveSoloRosterRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/roster"); AuthSchemes("Bearer");
        Description(x => x.WithName("LockLiveSoloRoster"));
        Summary(x => x.Summary = "Locks a captain-approved team roster before the Match starts.");
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(LockLiveSoloRosterRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await matches.RosterAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.TeamId, req.ExpectedStamp, req.UserIds, clock.GetUtcNow()), ct));
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class RequestLiveSoloRescueRequest
{
    public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } public Guid RoundId { get; set; }
    public Guid UserId { get; set; } public string? Reason { get; set; }
}
public sealed class RequestLiveSoloRescueValidator : Validator<RequestLiveSoloRescueRequest>
{
    public RequestLiveSoloRescueValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.RoundId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty(); RuleFor(x => x.Reason).MaximumLength(4000);
    }
}
public sealed class RequestLiveSoloRescueEndpoint(ILiveSoloDirectionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<RequestLiveSoloRescueRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/rescue-requests"); AuthSchemes("Bearer");
        Description(x => x.WithName("RequestLiveSoloRescue"));
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(RequestLiveSoloRescueRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await store.RequestRescueAsync(new(req.CompetitionId, req.MatchId, req.RoundId, user.UserId, req.UserId, req.Reason ?? "", clock.GetUtcNow()), ct));
}

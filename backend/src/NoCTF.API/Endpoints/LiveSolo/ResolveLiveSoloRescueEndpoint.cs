using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ResolveLiveSoloRescueRequest
{
    public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } public Guid RequestId { get; set; }
    public bool Approve { get; set; } public string? Reason { get; set; }
}
public sealed class ResolveLiveSoloRescueValidator : Validator<ResolveLiveSoloRescueRequest>
{
    public ResolveLiveSoloRescueValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(4000);
    }
}
public sealed class ResolveLiveSoloRescueEndpoint(ILiveSoloDirectionStore store, IUserContext user, TimeProvider clock)
    : Endpoint<ResolveLiveSoloRescueRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/rescue-requests/{requestId}/decision"); AuthSchemes("Bearer");
        Description(x => x.WithName("ResolveLiveSoloRescue"));
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(ResolveLiveSoloRescueRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await store.ResolveRescueAsync(new(req.CompetitionId, req.MatchId, req.RequestId, user.UserId, req.Approve, req.Reason ?? "", clock.GetUtcNow()), ct));
}

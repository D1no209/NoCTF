using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ConfigureLiveSoloDirectionPoolRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public IReadOnlyList<Guid> PresetIds { get; set; } = [];
}
public sealed class ConfigureLiveSoloDirectionPoolValidator : Validator<ConfigureLiveSoloDirectionPoolRequest>
{
    public ConfigureLiveSoloDirectionPoolValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleForEach(x => x.PresetIds).NotEmpty();
    }
}
public sealed class ConfigureLiveSoloDirectionPoolEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<ConfigureLiveSoloDirectionPoolRequest, Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/live-solo/matches/{matchId}/direction-pool"); AuthSchemes("Bearer");
        Description(x => x.WithName("ConfigureLiveSoloDirectionPool"));
    }
    public override async Task<Results<Ok<LiveSoloMatchResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(ConfigureLiveSoloDirectionPoolRequest req, CancellationToken ct) =>
        LiveSoloProtocol.MatchMutation(await store.ConfigurePoolAsync(req.CompetitionId, req.MatchId, user.UserId, req.PresetIds, ct));
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloConfigurationRequest
{
    public Guid CompetitionId { get; set; }
    public required LiveSoloConfigurationContract Configuration { get; set; }
}
public sealed class SaveLiveSoloConfigurationValidator : Validator<SaveLiveSoloConfigurationRequest>
{
    public SaveLiveSoloConfigurationValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.Configuration).NotNull();
        RuleFor(x => x.Configuration.StageRules).NotNull().When(x => x.Configuration is not null);
        RuleForEach(x => x.Configuration.StageRules).NotNull().When(x => x.Configuration?.StageRules is not null);
    }
}
public sealed class SaveLiveSoloConfigurationEndpoint(ManageLiveSoloConfiguration configuration, IUserContext user, TimeProvider clock)
    : Endpoint<SaveLiveSoloConfigurationRequest, Results<Ok<LiveSoloConfigurationContract>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Put("/competitions/{competitionId}/live-solo/configuration"); AuthSchemes("Bearer");
        Description(x => x.WithName("SaveLiveSoloConfiguration"));
        Summary(x => x.Summary = "Updates independent LiveSolo policy through the shared configuration use case; judges and observers remain read-only.");
    }
    public override async Task<Results<Ok<LiveSoloConfigurationContract>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(SaveLiveSoloConfigurationRequest req, CancellationToken ct)
    {
        var result = await configuration.SaveAsync(req.CompetitionId, user.UserId,
            LiveSoloConfigurationMapping.ToDomain(req.CompetitionId, req.Configuration), clock.GetUtcNow(), ct);
        return result.Failure switch {
            null when result.Configuration is not null => TypedResults.Ok(LiveSoloConfigurationMapping.ToContract(result.Configuration)),
            LiveSoloFailure.Forbidden => TypedResults.Forbid(), LiveSoloFailure.NotFound => TypedResults.NotFound(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict)) };
    }
}

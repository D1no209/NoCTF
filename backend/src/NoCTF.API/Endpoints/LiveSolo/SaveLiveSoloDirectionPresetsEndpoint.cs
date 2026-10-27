using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloDirectionPresetsRequest
{
    public Guid CompetitionId { get; set; }
    public string Direction { get; set; } = "";
    public IReadOnlyList<Guid> QuestionGroupIds { get; set; } = [];
}
public sealed class SaveLiveSoloDirectionPresetsValidator : Validator<SaveLiveSoloDirectionPresetsRequest>
{
    public SaveLiveSoloDirectionPresetsValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.Direction).NotEmpty().MaximumLength(96);
        RuleFor(x => x.QuestionGroupIds).NotEmpty(); RuleForEach(x => x.QuestionGroupIds).NotEmpty();
    }
}
public sealed class SaveLiveSoloDirectionPresetsEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<SaveLiveSoloDirectionPresetsRequest, Results<Ok<ListLiveSoloDirectionPresetsResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/direction-presets/batch"); AuthSchemes("Bearer");
        Description(x => x.WithName("SaveLiveSoloDirectionPresets"));
    }
    public override async Task<Results<Ok<ListLiveSoloDirectionPresetsResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(SaveLiveSoloDirectionPresetsRequest req, CancellationToken ct)
    {
        var result = await store.SavePresetsAsync(req.CompetitionId, user.UserId, req.Direction, req.QuestionGroupIds, ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(new ListLiveSoloDirectionPresetsResponse(result.Presets.Select(LiveSoloDirectionPresetResponse.From).ToArray())),
            LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration or LiveSoloFailure.QuestionGroupNotFound => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure.Value))
        };
    }
}

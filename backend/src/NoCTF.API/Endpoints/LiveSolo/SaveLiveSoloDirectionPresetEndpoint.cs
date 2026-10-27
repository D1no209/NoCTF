using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloDirectionPresetRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string Direction { get; set; } = "";
    public Guid QuestionGroupId { get; set; }
}
public sealed class SaveLiveSoloDirectionPresetValidator : Validator<SaveLiveSoloDirectionPresetRequest>
{
    public SaveLiveSoloDirectionPresetValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.QuestionGroupId).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(160); RuleFor(x => x.Direction).NotEmpty().MaximumLength(96);
    }
}
public sealed class SaveLiveSoloDirectionPresetEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<SaveLiveSoloDirectionPresetRequest, Results<Ok<LiveSoloDirectionPresetResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/direction-presets"); AuthSchemes("Bearer");
        Description(x => x.WithName("SaveLiveSoloDirectionPreset"));
    }
    public override async Task<Results<Ok<LiveSoloDirectionPresetResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(SaveLiveSoloDirectionPresetRequest req, CancellationToken ct)
    {
        var result = await store.SavePresetAsync(req.CompetitionId, user.UserId, new(req.Id, req.Name ?? "", req.Direction, req.QuestionGroupId), ct);
        return result.Failure switch
        {
            null when result.Preset != null => TypedResults.Ok(LiveSoloDirectionPresetResponse.From(result.Preset)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(),
            LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration or LiveSoloFailure.QuestionGroupNotFound => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}

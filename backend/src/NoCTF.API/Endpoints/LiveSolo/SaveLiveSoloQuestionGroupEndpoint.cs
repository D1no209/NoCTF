using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class SaveLiveSoloQuestionGroupRequest
{
    public Guid CompetitionId { get; set; }
    public Guid? Id { get; set; }
    public Guid? ExpectedStamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Reserve { get; set; }
    public int? LimitSeconds { get; set; }
    public IReadOnlyList<LiveSoloQuestionGroupEntryContract> Questions { get; set; } = [];
}
public sealed class SaveLiveSoloQuestionGroupValidator : Validator<SaveLiveSoloQuestionGroupRequest>
{
    public SaveLiveSoloQuestionGroupValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.ExpectedStamp).NotEmpty().When(x => x.Id.HasValue);
        RuleFor(x => x.LimitSeconds).InclusiveBetween(1, 86400).When(x => x.LimitSeconds.HasValue);
        RuleFor(x => x.Questions).NotEmpty().Must(x => x.Count <= 64);
    }
}
public sealed class SaveLiveSoloQuestionGroupEndpoint(ManageLiveSoloMatches matches, IUserContext user, TimeProvider clock)
    : Endpoint<SaveLiveSoloQuestionGroupRequest, Results<Ok<LiveSoloQuestionGroupResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/question-groups"); AuthSchemes("Bearer");
        Description(x => x.WithName("SaveLiveSoloQuestionGroup"));
        Summary(x => x.Summary = "Creates or replaces a staff question group; a group allocated to an active Round cannot be changed.");
    }
    public override async Task<Results<Ok<LiveSoloQuestionGroupResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(SaveLiveSoloQuestionGroupRequest req, CancellationToken ct)
    {
        var result = await matches.SaveGroupAsync(req.CompetitionId, user.UserId, new(req.Id, req.Name, req.Reserve, req.LimitSeconds,
            req.Questions.Select(x => new LiveSoloQuestionGroupEntry(x.CompetitionChallengeId, x.OpenOffsetSeconds)).ToArray(), req.ExpectedStamp), clock.GetUtcNow(), ct);
        return result.Failure switch
        {
            null when result.Group is not null => TypedResults.Ok(LiveSoloGroupProtocol.Group(result.Group)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}

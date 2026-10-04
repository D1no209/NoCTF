using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.ComponentModel.DataAnnotations;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Directions;
using NoCTF.Application.Teams.Moderation;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class SaveCompetitionDirectionsRequest
{
    public Guid CompetitionId { get; set; }
    public required IReadOnlyList<CompetitionDirectionResponse> Items { get; set; }
}
public sealed record CompetitionDirectionFailureResponse(
    [property: Required, JsonRequired, JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionDirectionFailure>))] CompetitionDirectionFailure Code,
    string Detail)
{
    [Required, JsonRequired]
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}
public sealed class SaveCompetitionDirectionsValidator : Validator<SaveCompetitionDirectionsRequest>
{
    public SaveCompetitionDirectionsValidator()
    {
        RuleFor(item => item.Items).NotNull().Must(items => items is not null && items.Count is >= 1 and <= 64);
        RuleForEach(item => item.Items).ChildRules(direction =>
        {
            direction.RuleFor(item => item.Id).NotEmpty();
            direction.RuleFor(item => item.Name).NotEmpty().MaximumLength(96);
            direction.RuleFor(item => item.Icon).NotEmpty().MaximumLength(80);
        });
    }
}
public sealed class SaveCompetitionDirectionsEndpoint(ManageCompetitionDirections directions,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<SaveCompetitionDirectionsRequest, Results<Ok<CompetitionDirectionsResponse>, NotFound, ForbidHttpResult,
        ProblemHttpResult, Conflict<CompetitionDirectionFailureResponse>>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/directions");
        AuthSchemes("Bearer");
        Summary(summary => { summary.Summary = "Saves competition challenge directions."; summary.Description = "Replaces the ordered direction catalog; rejects unavailable Lucide icons and removal of directions still referenced by challenges."; });
        Description(builder => builder.WithName("AdminSaveCompetitionDirections"));
    }
    public override async Task<Results<Ok<CompetitionDirectionsResponse>, NotFound, ForbidHttpResult,
        ProblemHttpResult, Conflict<CompetitionDirectionFailureResponse>>> ExecuteAsync(SaveCompetitionDirectionsRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await directions.SaveAsync(request.CompetitionId,
            request.Items.Select(item => new CompetitionDirectionView(item.Id, item.Name, item.Icon)).ToArray(), clock.GetUtcNow(), ct);
        return result.Failure switch
        {
            null => TypedResults.Ok(new CompetitionDirectionsResponse(result.Items!.Select(item => new CompetitionDirectionResponse(item.Id, item.Name, item.Icon)).ToArray())),
            CompetitionDirectionFailure.CompetitionNotFound => TypedResults.NotFound(),
            CompetitionDirectionFailure.DirectionInUse or CompetitionDirectionFailure.NameConflict => TypedResults.Conflict(new CompetitionDirectionFailureResponse(result.Failure.Value, Describe(result.Failure.Value))),
            _ => ApiProblems.Problem(statusCode: StatusCodes.Status400BadRequest, title: ApiMessages.Get(ApiMessageId.SaveCompetitionDirectionsTitleCompetitionDirectionsWereSaved),
                detail: ApiMessages.For(result.Failure), extensions: new Dictionary<string, object?> { ["code"] = result.Failure.Value.ToString() })
        };
    }
    private static string Describe(CompetitionDirectionFailure failure) => failure switch
    {
        CompetitionDirectionFailure.InvalidCatalog => "Provide 1–64 directions with nonempty, unique names and identifiers.",
        CompetitionDirectionFailure.InvalidIcon => "The icon suffix is not available in the installed Lucide package.",
        CompetitionDirectionFailure.DirectionInUse => "Reassign all challenges referencing this direction before removing it, including deleted challenges.",
        CompetitionDirectionFailure.NameConflict => "Direction names or identifiers conflict with another saved catalog.",
        CompetitionDirectionFailure.CompetitionNotFound => "Competition not found.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
    };

}

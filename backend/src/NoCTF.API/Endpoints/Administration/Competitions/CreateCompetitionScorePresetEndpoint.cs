using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class SaveCompetitionScorePresetRequest
{
    public Guid CompetitionId { get; set; }
    public required string Name { get; set; }
    public ScoreCurveContract? ScoreCurve { get; set; }
}
public sealed class SaveCompetitionScorePresetValidator : Validator<SaveCompetitionScorePresetRequest>
{
    public SaveCompetitionScorePresetValidator()
    {
        RuleFor(item => item.Name).NotEmpty().Must(name => name is not null && name.Trim().Length <= 40);
    }
}
public sealed class CreateCompetitionScorePresetEndpoint(ManageCompetitionScorePresets presets,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<SaveCompetitionScorePresetRequest, Results<Created<CompetitionScorePresetMutationResponse>, ForbidHttpResult,
        Conflict<CompetitionScorePresetFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/score-presets"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionScorePreset"));
        Summary(summary =>
        {
            summary.Response<CompetitionScorePresetMutationResponse>(201);
            summary.Response<CompetitionScorePresetFailureResponse>(409);
            summary.Response<Microsoft.AspNetCore.Mvc.ProblemDetails>(404);
        });
    }
    public override async Task<Results<Created<CompetitionScorePresetMutationResponse>, ForbidHttpResult,
        Conflict<CompetitionScorePresetFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        SaveCompetitionScorePresetRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await presets.SaveAsync(competitionId, null, request.Name,
            request.ScoreCurve is { } curve ? CompetitionScorePresetResponses.Curve(curve) : null, clock.GetUtcNow(), ct);
        if (result.Failure == CompetitionScorePresetFailure.NameConflict)
            return TypedResults.Conflict(new CompetitionScorePresetFailureResponse(result.Failure.Value));
        return result.Failure is { } failure ? CompetitionScorePresetResponses.Failure(failure)
            : TypedResults.Created($"/api/v1/admin/competitions/{competitionId}/score-presets/{result.Item!.Preset.Id}",
                CompetitionScorePresetResponses.Map(result));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionScorePresetEndpoint(ManageCompetitionScorePresets presets,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<SaveCompetitionScorePresetRequest, Results<Ok<CompetitionScorePresetMutationResponse>, ForbidHttpResult,
        Conflict<CompetitionScorePresetFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/score-presets/{presetId}"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionScorePreset"));
        Summary(summary =>
        {
            summary.Response<CompetitionScorePresetMutationResponse>(200);
            summary.Response<CompetitionScorePresetFailureResponse>(409);
            summary.Response<Microsoft.AspNetCore.Mvc.ProblemDetails>(404);
        });
    }
    public override async Task<Results<Ok<CompetitionScorePresetMutationResponse>, ForbidHttpResult,
        Conflict<CompetitionScorePresetFailureResponse>, ProblemHttpResult>> ExecuteAsync(
        SaveCompetitionScorePresetRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await presets.SaveAsync(competitionId, Route<Guid>("presetId"), request.Name,
            request.ScoreCurve is { } curve ? CompetitionScorePresetResponses.Curve(curve) : null, clock.GetUtcNow(), ct);
        if (result.Failure == CompetitionScorePresetFailure.NameConflict)
            return TypedResults.Conflict(new CompetitionScorePresetFailureResponse(result.Failure.Value));
        return result.Failure is { } failure ? CompetitionScorePresetResponses.Failure(failure)
            : TypedResults.Ok(CompetitionScorePresetResponses.Map(result));
    }
}

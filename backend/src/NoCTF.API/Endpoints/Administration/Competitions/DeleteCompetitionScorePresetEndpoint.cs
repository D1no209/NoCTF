using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Scoring;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionScorePresetRequest { public Guid CompetitionId { get; set; } public Guid PresetId { get; set; } }
public sealed class DeleteCompetitionScorePresetEndpoint(ManageCompetitionScorePresets presets,
    ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider clock)
    : Endpoint<DeleteCompetitionScorePresetRequest, Results<Ok<CompetitionScorePresetMutationResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/score-presets/{presetId}"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionScorePreset"));
        Summary(summary =>
        {
            summary.Response<CompetitionScorePresetMutationResponse>(200);
            summary.Response<Microsoft.AspNetCore.Mvc.ProblemDetails>(404);
        });
    }
    public override async Task<Results<Ok<CompetitionScorePresetMutationResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        DeleteCompetitionScorePresetRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await presets.DeleteAsync(request.CompetitionId, request.PresetId, clock.GetUtcNow(), ct);
        return result.Failure is { } failure ? CompetitionScorePresetResponses.Failure(failure)
            : TypedResults.Ok(CompetitionScorePresetResponses.Map(result));
    }
}

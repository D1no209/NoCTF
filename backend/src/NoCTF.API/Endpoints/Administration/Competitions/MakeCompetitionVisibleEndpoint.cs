using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class MakeCompetitionVisibleEndpoint(
    TransitionCompetitionLifecycle transition,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/make-visible");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminMakeCompetitionVisible")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Makes a competition visible.";
            summary.Description = "Moves a Draft or Published competition to Visible.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await transition.ExecuteAsync(
            competitionId,
            CompetitionStatus.Visible,
            user.UserId,
            "manual_make_visible",
            ct);
        if (result.ErrorCode == "competition_not_found")
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Competition cannot be made visible.",
                detail: result.ErrorMessage);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class StartCompetitionEndpoint(
    TransitionCompetitionLifecycle transition,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/start");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminStartCompetition")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Starts a published competition.";
            summary.Description = "Transitions the competition to Running and queues runtime provisioning.";
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
            CompetitionStatus.Running,
            user.UserId,
            "manual_start",
            ct);
        if (result.ErrorCode == "competition_not_found")
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Competition cannot be started.",
                detail: result.ErrorMessage);
    }
}

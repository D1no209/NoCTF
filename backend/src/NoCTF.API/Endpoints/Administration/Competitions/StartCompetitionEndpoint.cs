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
        Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionTransitionConflictResponse>>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/start");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminStartCompetition"));
        Summary(summary =>
        {
            summary.Summary = "Starts a published competition.";
            summary.Description = "Transitions the competition to Running and queues runtime provisioning.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionTransitionConflictResponse>>> ExecuteAsync(
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
        if (result.FailureCode == CompetitionTransitionFailureCode.CompetitionNotFound)
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Conflict(CompetitionTransitionConflictMapper.ToResponse(result));
    }
}

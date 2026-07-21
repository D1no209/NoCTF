using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class FinishCompetitionEndpoint(TransitionCompetitionLifecycle transition, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/finish"); AuthSchemes("Bearer"); Summary(s => s.Summary = "Finish a competition and schedule runtime cleanup."); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var result = await transition.ExecuteAsync(id, CompetitionStatus.Finished, user.UserId, "manual_finish", ct);
        if (result.ErrorCode == "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Competition cannot be finished.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

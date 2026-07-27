using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class PublishCompetitionEndpoint(TransitionCompetitionLifecycle transition, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/publish");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPublishCompetition")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Publishes a competition.";
            summary.Description = "Moves a competition through the documented publication lifecycle state machine.";
        });
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var result = await transition.ExecuteAsync(id, CompetitionStatus.Published, user.UserId, "manual_publish", ct);
        if (result.ErrorCode == "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Competition cannot be published.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

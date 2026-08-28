using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionEndpoint(DeleteCompetition delete, ICompetitionModerationAuthorizer authorizer, IUserContext user, TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetition")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Deletes a competition.";
            summary.Description = "Soft-deletes an inactive competition owned or managed by the caller.";
        });
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var id = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, id, ct)) return TypedResults.Forbid();
        var result = await delete.ExecuteAsync(id, user.UserId, timeProvider.GetUtcNow(), ct);
        if (result.FailureCode == CompetitionManagementFailureCode.CompetitionNotFound) return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Competition was not deleted.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}

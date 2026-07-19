using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Submissions;

public sealed class RebuildCompetitionRequest { public Guid CompetitionId { get; set; } }

public sealed class RebuildCompetitionEndpoint(
    IBackgroundWorkScheduler scheduler,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<RebuildCompetitionRequest, Results<Accepted<RebuildCompetitionResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/rebuild");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Accepted<RebuildCompetitionResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        RebuildCompetitionRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, request.CompetitionId, cancellationToken))
            return TypedResults.Forbid();
        await scheduler.EnqueueCompetitionRebuildAsync(request.CompetitionId, cancellationToken);
        return TypedResults.Accepted($"/admin/competitions/{request.CompetitionId:N}/rebuild",
            new RebuildCompetitionResponse(request.CompetitionId, "Processing"));
    }
}

public sealed record RebuildCompetitionResponse(Guid CompetitionId, string State);

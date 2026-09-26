using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class DeleteCompetitionBadgeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid BadgeId { get; set; }
}

public sealed record CompetitionBadgeConflictResponse(string Code, string Detail);

public sealed class DeleteCompetitionBadgeEndpoint(
    ManageCompetitionBadges badges,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<DeleteCompetitionBadgeRequest,
        Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionBadgeConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/badges/{badgeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionBadge"));
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<CompetitionBadgeConflictResponse>>>
        ExecuteAsync(DeleteCompetitionBadgeRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var failure = await badges.DeleteAsync(
            request.CompetitionId, request.BadgeId, clock.GetUtcNow(), ct);
        return failure switch
        {
            null => TypedResults.NoContent(),
            CompetitionBadgeFailure.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Conflict(new CompetitionBadgeConflictResponse(
                "BadgeInUse", "Badge is referenced by the progression graph."))
        };
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ListCompetitionBadgesRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed record CompetitionBadgeContract(
    Guid Id, Guid CompetitionId, string Name, string? Description,
    Guid ImageFileId, string ImageUrl,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CompetitionBadgeListContract(CompetitionBadgeContract[] Items);

public sealed class ListCompetitionBadgesEndpoint(
    ManageCompetitionBadges badges,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListCompetitionBadgesRequest,
        Results<Ok<CompetitionBadgeListContract>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/badges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionBadges"));
    }

    public override async Task<Results<Ok<CompetitionBadgeListContract>, NotFound, ForbidHttpResult>>
        ExecuteAsync(ListCompetitionBadgesRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var items = await badges.ListAsync(request.CompetitionId, ct);
        return items is null ? TypedResults.NotFound()
            : TypedResults.Ok(new CompetitionBadgeListContract(
                items.Select(CompetitionBadgeProtocol.ToContract).ToArray()));
    }
}

internal static class CompetitionBadgeProtocol
{
    public static CompetitionBadgeContract ToContract(CompetitionBadgeView badge) => new(
        badge.Id, badge.CompetitionId, badge.Name, badge.Description,
        badge.ImageFileId,
        $"/api/v1/competitions/{badge.CompetitionId}/badges/{badge.Id}/image?revision={badge.ImageFileId:N}",
        badge.CreatedAt, badge.UpdatedAt);
}

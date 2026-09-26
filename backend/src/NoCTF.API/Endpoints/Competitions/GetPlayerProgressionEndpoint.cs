using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetPlayerProgressionRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed record ProgressionBadgeDisplayContract(
    Guid Id, Guid CompetitionId, string CompetitionTitle,
    string Name, string? Description, string ImageUrl);
public sealed record PlayerProgressionNodeContract(
    Guid Id, ProgressionNodeKind Kind, Guid ResourceId,
    string Title, string? Description, string? Direction,
    bool Active, bool Complete, bool Visited, DateTimeOffset? FirstOpenedAt,
    string? ImageUrl);
public sealed record PlayerProgressionContract(
    bool Enabled, bool ShowPlayerMap, long Revision,
    PlayerProgressionNodeContract[] Nodes,
    NoCTF.API.Endpoints.Administration.Competitions.ProgressionEdgeContract[] Edges,
    ProgressionBadgeDisplayContract[] Badges);

public sealed class GetPlayerProgressionEndpoint(
    IProgressionPlayerReader progression,
    ICompetitionChallengeReadAccess readAccess,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<GetPlayerProgressionRequest,
        Results<Ok<PlayerProgressionContract>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/progression");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("GetPlayerCompetitionProgression"));
    }

    public override async Task<Results<Ok<PlayerProgressionContract>, NotFound>> ExecuteAsync(
        GetPlayerProgressionRequest request, CancellationToken ct)
    {
        var decision = await readAccess.ResolveAsync(
            user.UserId, request.CompetitionId, clock.GetUtcNow(), ct);
        if (decision is null || decision.Visibility.GameMode != GameMode.Ctf
            || !ParticipantChallengeVisibilityPolicy.CanView(
                decision.Visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var view = await progression.ReadAsync(
            request.CompetitionId, decision.TeamId, ct);
        return TypedResults.Ok(new PlayerProgressionContract(
            view.Enabled, view.ShowPlayerMap, view.Revision,
            view.Nodes.Select(node => new PlayerProgressionNodeContract(
                node.Id, node.Kind, node.ResourceId,
                node.Title, node.Description, node.Direction,
                node.Active, node.Complete, node.Visited, node.FirstOpenedAt,
                node.ImageFileId is null ? null
                    : $"/api/v1/competitions/{request.CompetitionId}/badges/{node.ResourceId}/image?revision={node.ImageFileId.Value:N}"))
                .ToArray(),
            view.Edges.Select(edge => new NoCTF.API.Endpoints.Administration.Competitions.ProgressionEdgeContract(
                edge.Id, edge.SourceNodeId, edge.TargetNodeId,
                edge.Condition)).ToArray(),
            view.Badges.Select(ProgressionBadgeDisplayProtocol.ToContract).ToArray()));
    }
}

internal static class ProgressionBadgeDisplayProtocol
{
    public static ProgressionBadgeDisplayContract ToContract(
        ProgressionBadgeDisplay badge) => new(
        badge.Id, badge.CompetitionId, badge.CompetitionTitle,
        badge.Name, badge.Description,
        $"/api/v1/competitions/{badge.CompetitionId}/badges/{badge.Id}/image?revision={badge.ImageFileId:N}");
}

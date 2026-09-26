using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Progression;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class GetCompetitionProgressionRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed record ProgressionChallengeNodeContract(Guid CompetitionChallengeId);
public sealed record ProgressionBadgeNodeContract(Guid CompetitionBadgeId);
public sealed record ProgressionNodeContract(
    Guid Id, ProgressionNodeKind Kind,
    ProgressionChallengeNodeContract? Challenge,
    ProgressionBadgeNodeContract? Badge,
    double PositionX, double PositionY);
public sealed record ProgressionEdgeContract(
    Guid Id, Guid SourceNodeId, Guid TargetNodeId,
    ProgressionPrerequisiteCondition Condition);
public sealed record CompetitionProgressionContract(
    Guid CompetitionId, bool Enabled, bool ShowPlayerMap,
    Guid? ConcurrencyStamp, long Revision,
    ProgressionNodeContract[] Nodes, ProgressionEdgeContract[] Edges);

public sealed class GetCompetitionProgressionEndpoint(
    GetCompetitionProgression get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetCompetitionProgressionRequest,
        Results<Ok<CompetitionProgressionContract>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/progression");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionProgression"));
    }

    public override async Task<Results<Ok<CompetitionProgressionContract>, NotFound, ForbidHttpResult>>
        ExecuteAsync(GetCompetitionProgressionRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var view = await get.ExecuteAsync(request.CompetitionId, ct);
        return view is null ? TypedResults.NotFound()
            : TypedResults.Ok(ProgressionProtocol.ToContract(view));
    }
}

internal static class ProgressionProtocol
{
    public static CompetitionProgressionContract ToContract(CompetitionProgressionView view) => new(
        view.CompetitionId, view.Enabled, view.ShowPlayerMap,
        view.ConcurrencyStamp, view.Revision,
        view.Nodes.Select(node => new ProgressionNodeContract(
            node.Id, node.Kind,
            node.Kind == ProgressionNodeKind.Challenge
                ? new(node.ResourceId) : null,
            node.Kind == ProgressionNodeKind.Badge
                ? new(node.ResourceId) : null,
            node.PositionX, node.PositionY)).ToArray(),
        view.Edges.Select(edge => new ProgressionEdgeContract(
            edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.Condition)).ToArray());
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Permissions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed record CompetitionPermissionsResponse(
    Guid CompetitionId,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<Guid> JudgeIds,
    IReadOnlyList<Guid> ObserverIds,
    int PermissionRevision);

internal static class CompetitionPermissionsMapper
{
    public static CompetitionPermissionsResponse ToResponse(
        CompetitionPermissionSnapshot snapshot) =>
        new(
            snapshot.CompetitionId,
            snapshot.OwnerId,
            snapshot.ManagerIds,
            snapshot.JudgeIds,
            snapshot.ObserverIds,
            snapshot.PermissionRevision);
}

public sealed class GetCompetitionPermissionsEndpoint(
    GetCompetitionPermissions get,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<
            Ok<CompetitionPermissionsResponse>,
            NotFound,
            ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/permissions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetCompetitionPermissions"));
        Summary(summary =>
        {
            summary.Summary = "Gets the complete competition permission snapshot.";
            summary.Description =
                "Returns owner, manager, judge, and observer assignments only to the competition owner or a platform administrator.";
        });
    }

    public override async Task<
        Results<
            Ok<CompetitionPermissionsResponse>,
            NotFound,
            ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.State switch
        {
            CompetitionPermissionSnapshotState.Found =>
                TypedResults.Ok(CompetitionPermissionsMapper.ToResponse(result.Snapshot!)),
            CompetitionPermissionSnapshotState.NotFound => TypedResults.NotFound(),
            CompetitionPermissionSnapshotState.Forbidden => TypedResults.Forbid(),
            _ => throw new InvalidOperationException(
                $"Unsupported competition permission snapshot state: {result.State}.")
        };
    }
}

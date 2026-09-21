using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed record RuntimeTargetResponse(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<RuntimeAccessResponse> Accesses);
public sealed record RuntimeTargetListResponse(IReadOnlyList<RuntimeTargetResponse> Items);

public sealed class ListRuntimeTargetsEndpoint(
    ListRuntimeTargets list,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<RuntimeTargetListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/targets");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Lists AWD participant targets.";
            summary.Description = "During hardening only the caller's team is returned; afterwards every eligible team is retained even when it has no running URLs.";
        });
    }

    public override async Task<Results<Ok<RuntimeTargetListResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var items = await list.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        if (items is null) return TypedResults.NotFound();
        return TypedResults.Ok(new RuntimeTargetListResponse(
            items.Select(item => new RuntimeTargetResponse(
                item.TeamId,
                item.TeamName,
                item.RuntimeInstanceId is Guid runtimeInstanceId
                    ? RuntimeAccessMapping.ToResponse(
                        runtimeInstanceId,
                        item.AccessMode,
                        item.Urls,
                        item.AccessEndpoints,
                        HttpContext.Request)
                    : [])).ToArray()));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;

namespace NoCTF.API.Endpoints.Runtime;

public sealed record RuntimeTargetResponse(Guid TeamId, string TeamName, IReadOnlyList<string> Urls);
public sealed record RuntimeTargetListResponse(IReadOnlyList<RuntimeTargetResponse> Items)
{
    public PublicAccessFailureProtocol? PublicAccessFailure { get; init; }
}

public sealed class ListRuntimeTargetsEndpoint(
    ListRuntimeTargets list,
    IUserContext user,
    TimeProvider timeProvider,
    ReadRuntimePublicAccess access)
    : EndpointWithoutRequest<Results<Ok<RuntimeTargetListResponse>, NotFound, ProblemHttpResult>>
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

    public override async Task<Results<Ok<RuntimeTargetListResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var items = await list.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        if (items is null) return TypedResults.NotFound();
        var route = await access.RouteAsync($"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}", ct);
        if (route is null) return RuntimeEndpointMapping.UnknownOrigin();
        return TypedResults.Ok(new RuntimeTargetListResponse(
            items.Select(item => new RuntimeTargetResponse(item.TeamId, item.TeamName,
                route == RuntimeAccessRoute.Direct ? item.Urls : [])).ToArray())
        { PublicAccessFailure = route == RuntimeAccessRoute.Gateway ? PublicAccessFailureProtocol.UnsupportedRuntimeKind : null });
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Runtime.Access;
using NoCTF.API.Security;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ProbeRuntimeProxyRequest
{
    public Guid RuntimeInstanceId { get; set; }
    public int BindingIndex { get; set; }
}

public sealed class ProbeRuntimeProxyEndpoint(
    IRuntimeProxyTargetReader targets,
    IExecutionScopeAccess? executionAccess = null,
    IUserContext? user = null,
    TimeProvider? clock = null)
    : Endpoint<ProbeRuntimeProxyRequest, Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Get("/runtime-proxies/{runtimeInstanceId}/{bindingIndex}");
        AllowAnonymous();
        Description(builder => builder.WithName("ProbeRuntimeProxy"));
        Summary(summary =>
        {
            summary.Summary = "Probes a Runtime TCP-over-WebSocket target.";
            summary.Description =
                "A valid running target returns no content; WebSocket upgrades are handled by the Runtime proxy transport.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(
        ProbeRuntimeProxyRequest request,
        CancellationToken cancellationToken)
    {
        var target = await targets.FindAsync(
            request.RuntimeInstanceId,
            request.BindingIndex,
            cancellationToken);
        if (target?.ExecutionScopeId is Guid executionScope)
        {
            if (target.CompetitionId is not Guid competitionId || target.CompetitionChallengeId is not Guid challengeId
                || user is null || user.UserId == Guid.Empty || executionAccess is null
                || !await executionAccess.CanAccessAsync(new(executionScope, competitionId, challengeId, target.TeamId,
                    user.UserId, ExecutionScopeOperation.Read, (clock ?? TimeProvider.System).GetUtcNow()), cancellationToken))
                return TypedResults.NotFound();
        }
        return target is null
            ? TypedResults.NotFound()
            : TypedResults.NoContent();
    }
}

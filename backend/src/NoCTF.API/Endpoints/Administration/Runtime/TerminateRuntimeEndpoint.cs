using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class TerminateRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound,
        ForbidHttpResult, Conflict<RuntimeConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.Terminate)));
        Description(builder => builder.WithName("AdminTerminateRuntime"));
        Summary(summary => summary.Summary = "Terminates an authorized Runtime instance.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound,
        ForbidHttpResult, Conflict<RuntimeConflictResponse>>> ExecuteAsync(CancellationToken ct)
    {
        var runtimeId = Route<Guid>("runtimeInstanceId");
        var current = await runtimes.GetPlatformAsync(runtimeId, ct);
        if (current is null)
            return TypedResults.NotFound();
        RuntimeMutationResult result;
        if (user.IsAdministrator)
        {
            result = await runtimes.TerminatePlatformAsync(
                runtimeId, user.UserId, timeProvider.GetUtcNow(), ct);
        }
        else
        {
            if (current.CompetitionId is not Guid competitionId
                || !await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
                return TypedResults.Forbid();
            result = await runtimes.TerminateAsync(
                competitionId, runtimeId, user.UserId,
                timeProvider.GetUtcNow(), ct);
        }
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
            return TypedResults.Conflict(new RuntimeConflictResponse(
                "The Runtime is already terminal or has no provider resource to clean up."));
        var response = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(response.StatusUrl, response);
    }
}

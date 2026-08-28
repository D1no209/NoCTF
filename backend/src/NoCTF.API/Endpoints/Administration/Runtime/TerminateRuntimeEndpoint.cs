using FastEndpoints;
using Microsoft.AspNetCore.Http;
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
    : EndpointWithoutRequest<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/terminate");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminTerminateRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Terminates an exact runtime instance.";
            summary.Description =
                "Queues durable provider cleanup for the selected instance.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await runtimes.TerminateAsync(
            competitionId,
            Route<Guid>("runtimeInstanceId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Runtime termination was rejected.",
                detail: "The runtime is already terminal or has no provider resource to clean up.");
        }

        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/competitions/{competitionId}/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}

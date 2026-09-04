using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class TerminatePlatformRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/runtimes/{runtimeInstanceId}/terminate");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformTerminateRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Terminates any active platform Runtime.";
            summary.Description =
                "Supports competition runtimes and challenge-template test runtimes through one platform operation.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var runtimeInstanceId = Route<Guid>("runtimeInstanceId");
        var result = await runtimes.TerminatePlatformAsync(
            runtimeInstanceId,
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Runtime termination was rejected.",
                detail: "The Runtime is already terminal or has no provider resource to clean up.");
        }

        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            "/api/v1/admin/platform/runtimes");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class ForceTerminateRuntimeRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ForceTerminateRuntimeValidator : Validator<ForceTerminateRuntimeRequest>
{
    public ForceTerminateRuntimeValidator()
    {
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
    }
}

public sealed class ForceTerminateRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    IUserContext user)
    : Endpoint<ForceTerminateRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/runtimes/{runtimeInstanceId}/force-terminate");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminForceTerminateRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Force-terminates a stuck runtime instance.";
            summary.Description =
                "Queues Runner-owned identity cleanup for a runtime stuck in Provisioning or Stopping for at least five minutes.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ForceTerminateRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var runtimeInstanceId = Route<Guid>("runtimeInstanceId");
        var result = await runtimes.ForceTerminateAsync(
            competitionId,
            runtimeInstanceId,
            user.UserId,
            request.Reason,
            DateTimeOffset.UtcNow,
            ct);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
        {
            var invalidReason = result.Failure == RuntimeMutationFailure.InvalidReason;
            return TypedResults.Problem(
                statusCode: invalidReason
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                title: invalidReason
                    ? "Force-termination reason is invalid."
                    : "Runtime force termination was rejected.",
                detail: result.Failure switch
                {
                    RuntimeMutationFailure.NotStuck =>
                        "The runtime is not an eligible long-running Provisioning or Stopping instance.",
                    _ => "A reason between 8 and 512 characters is required."
                });
        }

        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/competitions/{competitionId}/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ForceTerminatePlatformRuntimeRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ForceTerminatePlatformRuntimeValidator
    : Validator<ForceTerminatePlatformRuntimeRequest>
{
    public ForceTerminatePlatformRuntimeValidator()
    {
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
    }
}

public sealed class ForceTerminatePlatformRuntimeEndpoint(
    ManageAdminRuntimes runtimes,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ForceTerminatePlatformRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/runtimes/{runtimeInstanceId}/force-terminate");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformForceTerminateRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Force-terminates any stuck platform Runtime.";
            summary.Description =
                "Cleans a competition or challenge-template test Runtime by its Runner-owned resource identity.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ForceTerminatePlatformRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        var runtimeInstanceId = Route<Guid>("runtimeInstanceId");
        var result = await runtimes.ForceTerminatePlatformAsync(
            runtimeInstanceId,
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow(),
            cancellationToken);
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
                detail: result.Failure == RuntimeMutationFailure.NotStuck
                    ? "The Runtime is not an eligible long-running Provisioning or Stopping instance."
                    : "A reason between 8 and 512 characters is required.");
        }

        var value = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            "/api/v1/admin/platform/runtimes");
        return TypedResults.Accepted(value.StatusUrl, value);
    }
}

using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.Runtime;

public sealed class CreateForceTerminationRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class CreateForceTerminationValidator
    : Validator<CreateForceTerminationRequest>
{
    public CreateForceTerminationValidator() =>
        RuleFor(request => request.Reason).NotEmpty().MinimumLength(8).MaximumLength(512);
}

public sealed class CreateForceTerminationEndpoint(
    ManageAdminRuntimes runtimes,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateForceTerminationRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/runtimes/{runtimeInstanceId}/force-terminations");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.ForceTerminate)));
        Description(builder => builder.WithName("AdminCreateRuntimeForceTermination"));
        Summary(summary => summary.Summary = "Creates a force-termination request for a stuck Runtime.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound,
        Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateForceTerminationRequest request,
        CancellationToken ct)
    {
        var result = await runtimes.ForceTerminatePlatformAsync(
            Route<Guid>("runtimeInstanceId"),
            user.UserId,
            request.Reason,
            timeProvider.GetUtcNow(),
            ct);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Failure == RuntimeMutationFailure.InvalidReason)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Force-termination reason is invalid.");
        if (result.Runtime is null)
            return TypedResults.Conflict(new RuntimeConflictResponse(
                "The Runtime is not an eligible long-running Provisioning or Stopping instance."));
        var response = new RuntimeAcceptedResponse(
            result.Runtime.Id,
            $"/api/v1/admin/runtimes/{result.Runtime.Id}");
        return TypedResults.Accepted(response.StatusUrl, response);
    }
}

using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordControlResultRequest
{
    [JsonRequired, JsonConverter(typeof(StrictPascalCaseEnumConverter<CapabilityExecutionState>))]
    public CapabilityExecutionState ExecutionState { get; set; }
    [JsonRequired, JsonConverter(typeof(StrictPascalCaseEnumConverter<ControlResult>))]
    public ControlResult Result { get; set; }
    public string? ControllerIdentifier { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<CapabilityExecutionFailure>))]
    public CapabilityExecutionFailure? ExecutionFailure { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class RecordControlResultValidator : Validator<RecordControlResultRequest>
{
    public RecordControlResultValidator()
    {
        RuleFor(value => value.ExecutionState).IsInEnum();
        RuleFor(value => value.Result).IsInEnum();
        RuleFor(value => value.ExecutionFailure).IsInEnum().When(value => value.ExecutionFailure.HasValue);
        RuleFor(value => value.ControllerIdentifier).MaximumLength(4096);
        RuleFor(value => value.ObservedAt).NotEmpty();
    }
}

public sealed class RecordControlResultEndpoint(IControlResultStore executions)
    : Endpoint<RecordControlResultRequest, Results<Ok<InternalResultResponse>, NotFound, Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/api/internal/v1/capabilities/control-results");
        RoutePrefixOverride(string.Empty);
        AuthSchemes("Internal"); Policies("CapabilityControlResult");
    }

    public override async Task<Results<Ok<InternalResultResponse>, NotFound, Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
        ExecuteAsync(RecordControlResultRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("execution_id"), out var executionId)
            || !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId)
            || !Enum.TryParse<CapabilityExecutionScope>(User.FindFirstValue("execution_scope"), out var scope) || !Enum.IsDefined(scope)
            || !long.TryParse(User.FindFirstValue("deadline_ticks"), out var deadlineTicks)
            || deadlineTicks < DateTimeOffset.MinValue.UtcTicks || deadlineTicks > DateTimeOffset.MaxValue.UtcTicks
            || User.FindFirstValue("resource") != $"control:{scope}:{executionId:D}:runtime:{runtimeId:D}"
            || (User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")) is not { Length: > 0 } runnerId)
            return TypedResults.Unauthorized();
        var disposition = await executions.RecordAsync(executionId, runtimeId, scope, runnerId, new(deadlineTicks, TimeSpan.Zero),
            new(request.ExecutionState, request.Result, request.ControllerIdentifier, request.ExecutionFailure, request.ObservedAt), ct);
        if (disposition == InternalResultDisposition.NotFound) return TypedResults.NotFound();
        var response = new InternalResultResponse(InternalResultProtocolMapper.ToProtocol(disposition));
        return disposition == InternalResultDisposition.Conflict ? TypedResults.Conflict(response) : TypedResults.Ok(response);
    }
}

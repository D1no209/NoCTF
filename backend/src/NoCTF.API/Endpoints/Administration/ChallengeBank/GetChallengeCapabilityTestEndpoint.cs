using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using System.Text.Json.Serialization;
using NoCTF.API.Serialization;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetChallengeCapabilityTestRequest
{
    public Guid ChallengeId { get; set; }
    public Guid ExecutionId { get; set; }
}

public sealed class GetChallengeCapabilityTestValidator : Validator<GetChallengeCapabilityTestRequest>
{
    public GetChallengeCapabilityTestValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.ExecutionId).NotEmpty();
    }
}

public sealed record TemplateCheckProbeResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckItemKind>))] CheckItemKind Kind, Guid? FlagId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckProbeResult>))] CheckProbeResult Result,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckFailureReason>))] CheckFailureReason Reason);

public sealed record ChallengeCapabilityTestResponse(Guid ExecutionId, Guid RuntimeInstanceId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<TemplateCapabilityTestKind>))] TemplateCapabilityTestKind Kind,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<TemplateCapabilityTestState>))] TemplateCapabilityTestState State,
    DateTimeOffset AcceptedAt, DateTimeOffset Deadline, DateTimeOffset? CompletedAt,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<CapabilityExecutionFailure>))] CapabilityExecutionFailure? Failure,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<TemplateCapabilityTestState>))] TemplateCapabilityTestState? FailureStage,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<FixResult>))] FixResult? FixResult,
    int? FixExitCode, bool FixExecutionUncertain, bool CheckCompleted, IReadOnlyList<TemplateCheckProbeResponse> Probes);

public sealed class GetChallengeCapabilityTestEndpoint(ITemplateCapabilityTestStore tests, IUserContext user)
    : Endpoint<GetChallengeCapabilityTestRequest, Results<Ok<ChallengeCapabilityTestResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/capability-tests/{executionId}");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankGetCapabilityTest"));
    }

    public override async Task<Results<Ok<ChallengeCapabilityTestResponse>, NotFound>> ExecuteAsync(GetChallengeCapabilityTestRequest request, CancellationToken ct)
    {
        var result = await tests.FindAsync(new(request.ChallengeId, user.UserId, user.IsAdministrator), request.ExecutionId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new ChallengeCapabilityTestResponse(result.ExecutionId, result.RuntimeInstanceId,
            result.Kind, result.State, result.AcceptedAt, result.Deadline, result.CompletedAt, result.Failure, result.FailureStage, result.FixResult, result.FixExitCode,
            result.FixExecutionUncertain, result.CheckCompleted, result.Probes.Select(probe => new TemplateCheckProbeResponse(probe.Kind, probe.FlagId, probe.Result, probe.Reason)).ToArray()));
    }
}

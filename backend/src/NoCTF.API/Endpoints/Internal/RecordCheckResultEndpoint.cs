using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using Riok.Mapperly.Abstractions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Challenges.Capabilities;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordCheckResultRequest
{
    public List<CheckProbeEvidenceRequest> Probes { get; set; } = [];
    public List<CheckFlagContextRequest> FlagContexts { get; set; } = [];
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class CheckFlagContextRequest
{
    public Guid FlagId { get; set; }
    public string? PrivateReadContext { get; set; }
    public string? PublicLocator { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<InternalResultDispositionProtocol>))]
public enum InternalResultDispositionProtocol { Applied, Duplicate, Superseded, Conflict }

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class InternalResultProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial InternalResultDispositionProtocol ToProtocol(InternalResultDisposition value);
}

public sealed record InternalResultResponse(InternalResultDispositionProtocol Disposition, string? Detail = null);

public sealed class CheckProbeEvidenceRequest
{
    [System.ComponentModel.DataAnnotations.Required, JsonRequired]
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckItemKind>))]
    public CheckItemKind Kind { get; set; }
    public Guid? FlagId { get; set; }
    [System.ComponentModel.DataAnnotations.Required, JsonRequired]
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckProbeResult>))]
    public CheckProbeResult Result { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckFailureReason>))]
    public CheckFailureReason Reason { get; set; }
}

public sealed class RecordCheckResultValidator : Validator<RecordCheckResultRequest>
{
    public RecordCheckResultValidator()
    {
        RuleFor(request => request.ObservedAt).NotEmpty();
        RuleFor(request => request.Probes).NotNull();
        RuleFor(request => request.FlagContexts).NotNull();
        RuleForEach(request => request.FlagContexts).NotNull().ChildRules(context =>
        {
            context.RuleFor(value => value.FlagId).NotEmpty();
            context.RuleFor(value => value.PrivateReadContext).MaximumLength(8192);
            context.RuleFor(value => value.PublicLocator).MaximumLength(2048);
        });
        RuleForEach(request => request.Probes).NotNull().ChildRules(probe =>
        {
            probe.RuleFor(value => value.Kind).IsInEnum();
            probe.RuleFor(value => value.Result).IsInEnum();
            probe.RuleFor(value => value.Reason).IsInEnum();
        });
    }
}

public sealed class RecordCheckResultEndpoint(RecordInternalResult record,
    NoCTF.Application.Challenges.Testing.ITemplateCapabilityCheckResultStore tests, TimeProvider timeProvider)
    : Endpoint<RecordCheckResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/api/internal/v1/capabilities/check-results");
        AuthSchemes("Internal");
        Policies("CapabilityCheckResult");
        RoutePrefixOverride(string.Empty);
        Summary(summary =>
        {
            summary.Summary = "Records claim-bound Check evidence.";
            summary.Description = "Execution scope, identity and Runtime come exclusively from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        RecordCheckResultRequest request,
        CancellationToken ct)
    {
        var permissions = User.FindAll("permission").Where(claim => claim.Value is "fix:check-result:write"
            or "template:check-result:write" or "awd:check-result:write").ToArray();
        if (permissions.Length != 1) return TypedResults.Unauthorized();
        var probes = request.Probes.Select(probe => new CheckProbeEvidence(probe.Kind, probe.FlagId, probe.Result, probe.Reason)).ToArray();
        var contexts = request.FlagContexts.Select(context => new CheckFlagContextEvidence(context.FlagId, context.PrivateReadContext, context.PublicLocator)).ToArray();
        if (permissions[0].Value == "awd:check-result:write")
        {
            if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var awdRuntimeId)
                || !Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var awdFactId)
                || !Guid.TryParse(User.FindFirstValue("attempt_id"), out var attemptId)
                || User.FindFirstValue("resource") != $"runtime:{awdRuntimeId:D}"
                || !long.TryParse(User.FindFirstValue("receipt_deadline"), out var receiptDeadline)
                || timeProvider.GetUtcNow().UtcTicks - TimeSpan.FromHours(24).Ticks > receiptDeadline)
                return TypedResults.Unauthorized();
            return Map(await record.AwdCapabilityCheckAsync(new(awdRuntimeId, awdFactId, attemptId,
                probes, contexts, request.ObservedAt, timeProvider.GetUtcNow()), ct));
        }
        if (Guid.TryParse(User.FindFirstValue("template_test_id"), out var executionId))
        {
            if (!User.HasClaim("permission", "template:check-result:write")
                || !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var testRuntimeId)
                || User.FindFirstValue("resource") != $"template-test:{executionId:D}:runtime:{testRuntimeId:D}"
                || !long.TryParse(User.FindFirstValue("deadline"), out var testDeadline)
                || timeProvider.GetUtcNow().ToUnixTimeSeconds() > testDeadline)
                return TypedResults.Unauthorized();
            var runnerId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(runnerId)) return TypedResults.Unauthorized();
            return Map(await tests.RecordCheckAsync(testRuntimeId, executionId, runnerId,
                probes, contexts, request.ObservedAt, ct));
        }
        if (permissions[0].Value != "fix:check-result:write") return TypedResults.Unauthorized();
        if (contexts.Length != 0) return TypedResults.Conflict(new InternalResultResponse(InternalResultDispositionProtocol.Conflict,
            "This Fix verification has no Flag-context requirements."));
        if (!Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId) ||
            !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeInstanceId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"gameplay-fact:{gameplayFactId:D}:runtime:{runtimeInstanceId:D}",
                StringComparison.Ordinal) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            timeProvider.GetUtcNow().ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var disposition = await record.FixCheckAsync(new(
            gameplayFactId,
            runtimeInstanceId,
            probes,
            request.ObservedAt,
            timeProvider.GetUtcNow()), ct);
        return Map(disposition);
    }

    private static Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
        Conflict<InternalResultResponse>, UnauthorizedHttpResult> Map(InternalResultDisposition disposition) => disposition switch
        {
            InternalResultDisposition.Applied or InternalResultDisposition.Duplicate =>
                TypedResults.Ok(new InternalResultResponse(
                    InternalResultProtocolMapper.ToProtocol(disposition))),
            InternalResultDisposition.Superseded =>
                TypedResults.Accepted(
                    uri: (string?)null,
                    value: new InternalResultResponse(
                        InternalResultDispositionProtocol.Superseded)),
            InternalResultDisposition.NotFound => TypedResults.NotFound(),
            InternalResultDisposition.Conflict => TypedResults.Conflict(
                new InternalResultResponse(
                    InternalResultDispositionProtocol.Conflict,
                    "The result conflicts with the execution or Runtime bound to this Check.")),
            _ => throw new InvalidOperationException(
                $"Unsupported Check result disposition: {disposition}.")
        };
}

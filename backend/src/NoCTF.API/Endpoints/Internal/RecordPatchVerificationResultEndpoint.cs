using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Internal;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PatchVerificationResultOutcome>))]
public enum PatchVerificationResultOutcome
{
    StillExploitable,
    Verified,
    ServiceAbnormal
}

public sealed class RecordPatchVerificationResultRequest
{
    public PatchVerificationResultOutcome Outcome { get; set; }
}

public sealed class RecordPatchVerificationResultValidator
    : Validator<RecordPatchVerificationResultRequest>
{
    public RecordPatchVerificationResultValidator() =>
        RuleFor(request => request.Outcome).IsInEnum();
}

public sealed class RecordPatchVerificationResultEndpoint(
    RecordInternalResult record,
    TimeProvider timeProvider)
    : Endpoint<RecordPatchVerificationResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/api/internal/v1/patch-verification/results");
        AuthSchemes("Internal");
        Policies("PatchVerificationResult");
        RoutePrefixOverride(string.Empty);
        Summary(summary =>
        {
            summary.Summary = "Records a claim-bound PatchVerification result.";
            summary.Description = "GameplayFactId comes exclusively from the internal JWT.";
        });
    }

    public override async Task<Results<Ok<InternalResultResponse>,
        Accepted<InternalResultResponse>, NotFound, Conflict<InternalResultResponse>,
        UnauthorizedHttpResult>> ExecuteAsync(
        RecordPatchVerificationResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId)
            || !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeInstanceId)
            || !string.Equals(
                User.FindFirstValue("resource"),
                $"gameplay-fact:{gameplayFactId:D}:runtime:{runtimeInstanceId:D}",
                StringComparison.Ordinal)
            || !long.TryParse(User.FindFirstValue("deadline"), out var deadline)
            || timeProvider.GetUtcNow().ToUnixTimeSeconds() > deadline)
        {
            return TypedResults.Unauthorized();
        }

        var outcome = request.Outcome switch
        {
            PatchVerificationResultOutcome.StillExploitable => AwdpFixOutcome.ExploitSucceeded,
            PatchVerificationResultOutcome.Verified => AwdpFixOutcome.DefenseSucceeded,
            PatchVerificationResultOutcome.ServiceAbnormal => AwdpFixOutcome.ServiceAbnormal,
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Outcome, null)
        };
        var disposition = await record.PatchVerificationAsync(AwdpFixResult.Create(
            gameplayFactId,
            runtimeInstanceId,
            outcome,
            timeProvider.GetUtcNow()), ct);
        return disposition switch
        {
            InternalResultDisposition.Applied or InternalResultDisposition.Duplicate =>
                TypedResults.Ok(new InternalResultResponse(
                    InternalResultProtocolMapper.ToProtocol(disposition))),
            InternalResultDisposition.Superseded => TypedResults.Accepted(
                uri: (string?)null,
                value: new InternalResultResponse(
                    InternalResultDispositionProtocol.Superseded)),
            InternalResultDisposition.NotFound => TypedResults.NotFound(),
            InternalResultDisposition.Conflict => TypedResults.Conflict(
                new InternalResultResponse(
                    InternalResultDispositionProtocol.Conflict,
                    "The result conflicts with the gameplay fact or Runtime bound to this PatchVerification.")),
            _ => throw new InvalidOperationException(
                $"Unsupported PatchVerification result disposition: {disposition}.")
        };
    }
}

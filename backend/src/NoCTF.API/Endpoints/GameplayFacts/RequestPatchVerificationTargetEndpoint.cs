using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Admission;
using NoCTF.Application.GameplayFacts.PatchVerification;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class RequestPatchVerificationTargetRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<PatchVerificationTargetFailureCodeProtocol>))]
public enum PatchVerificationTargetFailureCodeProtocol
{
    PatchVerificationNotAvailable,
    ExperimentalFeatureDisabled,
    ActiveTargetExists,
    PatchAlreadyVerified,
    PatchAttemptsExhausted,
    RuntimeQuotaExceeded,
    InvalidRuntimeConfiguration,
    TargetConcurrency
}

public sealed record PatchVerificationTargetFailureResponse(
    PatchVerificationTargetFailureCodeProtocol Code,
    string Detail);

internal static class PatchVerificationTargetProtocolMapping
{
    public static PatchVerificationTargetFailureCodeProtocol ToProtocol(
        PatchVerificationTargetFailureCode code) => code switch
        {
            PatchVerificationTargetFailureCode.PatchVerificationNotAvailable => PatchVerificationTargetFailureCodeProtocol.PatchVerificationNotAvailable,
            PatchVerificationTargetFailureCode.ExperimentalFeatureDisabled => PatchVerificationTargetFailureCodeProtocol.ExperimentalFeatureDisabled,
            PatchVerificationTargetFailureCode.ActiveTargetExists => PatchVerificationTargetFailureCodeProtocol.ActiveTargetExists,
            PatchVerificationTargetFailureCode.PatchAlreadyVerified => PatchVerificationTargetFailureCodeProtocol.PatchAlreadyVerified,
            PatchVerificationTargetFailureCode.PatchAttemptsExhausted => PatchVerificationTargetFailureCodeProtocol.PatchAttemptsExhausted,
            PatchVerificationTargetFailureCode.RuntimeQuotaExceeded => PatchVerificationTargetFailureCodeProtocol.RuntimeQuotaExceeded,
            PatchVerificationTargetFailureCode.InvalidRuntimeConfiguration => PatchVerificationTargetFailureCodeProtocol.InvalidRuntimeConfiguration,
            PatchVerificationTargetFailureCode.TargetConcurrency => PatchVerificationTargetFailureCodeProtocol.TargetConcurrency,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
        };
}

public sealed record RequestPatchVerificationTargetResponse(
    Guid RuntimeInstanceId,
    RuntimeStateProtocol State,
    string StatusUrl);

public sealed class RequestPatchVerificationTargetEndpoint(
    RequestPatchVerificationTarget requestTarget,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<RequestPatchVerificationTargetRequest,
        Results<Accepted<RequestPatchVerificationTargetResponse>, NotFound,
            Conflict<PatchVerificationTargetFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification-targets");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand)));
        Options(options => options.WithMetadata(new EnableRateLimitingAttribute("submission")));
        Options(options => options.WithMetadata(
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        Summary(summary =>
        {
            summary.Summary = "Requests a clean one-shot CTF PatchVerification target.";
            summary.Description = "Creates one isolated target for one team and challenge.";
        });
    }

    public override async Task<Results<Accepted<RequestPatchVerificationTargetResponse>,
        NotFound, Conflict<PatchVerificationTargetFailureResponse>>> ExecuteAsync(
        RequestPatchVerificationTargetRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var result = await requestTarget.ExecuteAsync(
            competitionId,
            competitionChallengeId,
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        if (result.FailureCode == PatchVerificationTargetFailureCode.PatchVerificationNotAvailable)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Conflict(new PatchVerificationTargetFailureResponse(
                PatchVerificationTargetProtocolMapping.ToProtocol(result.FailureCode!.Value),
                result.ErrorMessage ?? "The PatchVerification target could not be requested."));
        }

        var statusUrl =
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification";
        return TypedResults.Accepted(statusUrl, new RequestPatchVerificationTargetResponse(
            result.Value!.RuntimeInstanceId,
            RuntimeStateProtocol.Queued,
            statusUrl));
    }
}

using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Awdp;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpDefenseTargetRequestFailureCodeProtocol>))]
public enum AwdpDefenseTargetRequestFailureCodeProtocol
{
    DefenseNotAvailable,
    ActiveDefenseTargetExists,
    DefenseAlreadySucceeded,
    BreakRequired,
    FixAttemptsExhausted,
    InvalidRuntimeConfiguration,
    DefenseTargetConcurrency
}

public sealed record RequestAwdpDefenseTargetResponse(
    Guid RuntimeInstanceId,
    RuntimeStateProtocol State,
    string StatusUrl);

public sealed record AwdpDefenseTargetConflictResponse(
    AwdpDefenseTargetRequestFailureCodeProtocol Code,
    string Detail);

[Mapper]
internal static partial class AwdpDefenseTargetRequestMapping
{
    [MapEnum(EnumMappingStrategy.ByName)]
    internal static partial AwdpDefenseTargetRequestFailureCodeProtocol ToProtocol(
        AwdpDefenseTargetRequestFailureCode value);
}

public sealed class RequestAwdpDefenseTargetEndpoint(
    RequestAwdpDefenseTarget requestTarget,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RequestAwdpDefenseTargetResponse>,
        NotFound, Conflict<AwdpDefenseTargetConflictResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new EnableRateLimitingAttribute("submission")));
        Summary(summary =>
        {
            summary.Summary = "Request a clean one-shot AWDP defense target.";
            summary.Description =
                "Creates an isolated disposable target that accepts exactly one Fix archive.";
        });
    }

    public override async Task<Results<Accepted<RequestAwdpDefenseTargetResponse>,
        NotFound, Conflict<AwdpDefenseTargetConflictResponse>>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var result = await requestTarget.ExecuteAsync(
            competitionId,
            competitionChallengeId,
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (result.FailureCode == AwdpDefenseTargetRequestFailureCode.DefenseNotAvailable)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Conflict(new AwdpDefenseTargetConflictResponse(
                AwdpDefenseTargetRequestMapping.ToProtocol(result.FailureCode!.Value),
                result.ErrorMessage ?? "The one-shot defense target could not be requested."));
        }

        var statusUrl =
            $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state";
        return TypedResults.Accepted(
            statusUrl,
            new RequestAwdpDefenseTargetResponse(
                result.Value!.RuntimeInstanceId,
                RuntimeStateProtocol.Queued,
                statusUrl));
    }
}

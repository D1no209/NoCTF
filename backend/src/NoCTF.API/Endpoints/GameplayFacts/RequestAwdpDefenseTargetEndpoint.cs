using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Awdp;
using Riok.Mapperly.Abstractions;
using NoCTF.Application.Admission;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class RequestAwdpDefenseTargetRequest
{
    /// <summary>One-time verification token; optional when platform policy disables verification. Maximum 4096 characters.</summary>
    [FromHeader("X-NoCTF-Human-Verification", IsRequired = false, RemoveFromSchema = true)]
    public string? HumanVerificationToken { get; set; }

    [RouteParam]
    public Guid CompetitionId { get; set; }
    [RouteParam]
    public Guid CompetitionChallengeId { get; set; }
}

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
    string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

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
    : Endpoint<RequestAwdpDefenseTargetRequest, Results<Accepted<RequestAwdpDefenseTargetResponse>,
        NotFound, Conflict<AwdpDefenseTargetConflictResponse>>>
{
    public override void Configure()
    {

        Description(builder => builder
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable));

        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(options => options.WithMetadata(
            new EnableRateLimitingAttribute("submission")));
        Options(options => options.WithMetadata(
            new HumanVerificationMetadata(HumanVerificationAction.Evaluation)));
        Summary(summary =>
        {
            summary.Params["X-NoCTF-Human-Verification"] = "One-time verification token, at most 4096 characters. Required only when the configured platform policy enables verification for this operation.";
            summary.Summary = "Request a clean one-shot AWDP defense target.";
            summary.Description =
                "Creates an isolated disposable target that accepts exactly one Fix archive.";
        });
    }

    public override async Task<Results<Accepted<RequestAwdpDefenseTargetResponse>,
        NotFound, Conflict<AwdpDefenseTargetConflictResponse>>> ExecuteAsync(
        RequestAwdpDefenseTargetRequest request, CancellationToken cancellationToken)
    {
        var competitionId = request.CompetitionId;
        var competitionChallengeId = request.CompetitionChallengeId;
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

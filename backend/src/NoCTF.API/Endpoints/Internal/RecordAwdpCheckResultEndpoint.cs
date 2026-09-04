using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdpCheckResultRequest
{
    public AwdpFixResultOutcome Outcome { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpFixResultOutcome>))]
public enum AwdpFixResultOutcome
{
    ExploitSucceeded,
    DefenseSucceeded,
    ServiceAbnormal
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Source)]
internal static partial class AwdpFixOutcomeMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial AwdpFixOutcome ToDomain(AwdpFixResultOutcome value);
}

public sealed class RecordAwdpCheckResultValidator : Validator<RecordAwdpCheckResultRequest>
{
    public RecordAwdpCheckResultValidator() =>
        RuleFor(request => request.Outcome).IsInEnum();
}

public sealed class RecordAwdpCheckResultEndpoint(RecordInternalResult record, TimeProvider timeProvider)
    : Endpoint<RecordAwdpCheckResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/api/internal/v1/awdp/fix-results");
        AuthSchemes("Internal");
        Policies("AwdpFixResult");
        RoutePrefixOverride(string.Empty);
        Summary(summary =>
        {
            summary.Summary = "Records a claim-bound AWDP fix result.";
            summary.Description = "GameplayFactId comes exclusively from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdpCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId) ||
            !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeInstanceId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"gameplay-fact:{gameplayFactId:D}:runtime:{runtimeInstanceId:D}",
                StringComparison.Ordinal) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            timeProvider.GetUtcNow().ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var disposition = await record.AwdpAsync(AwdpFixResult.Create(
            gameplayFactId,
            runtimeInstanceId,
            AwdpFixOutcomeMapper.ToDomain(request.Outcome),
            timeProvider.GetUtcNow()), ct);
        return disposition switch
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
                    "The result conflicts with the gameplay fact or runtime currently bound to this Fix verification.")),
            _ => throw new InvalidOperationException(
                $"Unsupported AWDP result disposition: {disposition}.")
        };
    }
}

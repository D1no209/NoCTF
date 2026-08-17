using System.Security.Claims;
using System.Text.Json;
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
    [JsonConverter(typeof(AwdpFixOutcomeJsonConverter))]
    public AwdpFixResultOutcome Outcome { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpFixResultOutcome>))]
public enum AwdpFixResultOutcome
{
    ExploitSucceeded,
    DefenseSucceeded,
    ServiceAbnormal
}

public sealed class AwdpFixOutcomeJsonConverter : JsonConverter<AwdpFixResultOutcome>
{
    public override AwdpFixResultOutcome Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("AWDP fix outcome must be a string.");
        return reader.GetString() switch
        {
            nameof(AwdpFixResultOutcome.ExploitSucceeded) or "StillVulnerable" =>
                AwdpFixResultOutcome.ExploitSucceeded,
            nameof(AwdpFixResultOutcome.DefenseSucceeded) or "Fixed" =>
                AwdpFixResultOutcome.DefenseSucceeded,
            nameof(AwdpFixResultOutcome.ServiceAbnormal) or "RuleViolation" or "ServiceUnavailable" =>
                AwdpFixResultOutcome.ServiceAbnormal,
            _ => throw new JsonException("AWDP fix outcome is invalid.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        AwdpFixResultOutcome value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
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

public sealed class RecordAwdpCheckResultEndpoint(RecordInternalResult record)
    : Endpoint<RecordAwdpCheckResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>>
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
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdpCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId) ||
            !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeInstanceId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"gameplay-fact:{gameplayFactId:D}:runtime:{runtimeInstanceId:D}",
                StringComparison.Ordinal) ||
            !int.TryParse(User.FindFirstValue("generation"), out var generation) ||
            !long.TryParse(
                User.FindFirstValue("runtime_processing_version"),
                out var runtimeProcessingVersion) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var disposition = await record.AwdpAsync(AwdpFixResult.Create(
            gameplayFactId,
            runtimeInstanceId,
            generation,
            runtimeProcessingVersion,
            AwdpFixOutcomeMapper.ToDomain(request.Outcome),
            DateTimeOffset.UtcNow), ct);
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
            _ => TypedResults.Conflict()
        };
    }
}

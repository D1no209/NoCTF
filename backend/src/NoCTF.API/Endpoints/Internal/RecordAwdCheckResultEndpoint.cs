using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdCheckResultRequest
{
    public AwdServiceStateProtocol State { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdServiceStateProtocol>))]
public enum AwdServiceStateProtocol
{
    Unknown,
    Up,
    Down,
    CheckerAbnormalExit,
    CheckerTimedOut
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<InternalResultDispositionProtocol>))]
public enum InternalResultDispositionProtocol
{
    Applied,
    Duplicate,
    Superseded
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class InternalResultProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial AwdServiceState ToDomain(AwdServiceStateProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial InternalResultDispositionProtocol ToProtocol(
        InternalResultDisposition value);
}

public sealed class RecordAwdCheckResultValidator : Validator<RecordAwdCheckResultRequest>
{
    public RecordAwdCheckResultValidator() =>
        RuleFor(request => request.State).IsInEnum();
}

public sealed record InternalResultResponse(InternalResultDispositionProtocol Disposition);

public sealed class RecordAwdCheckResultEndpoint(RecordInternalResult record, TimeProvider timeProvider)
    : Endpoint<RecordAwdCheckResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/api/internal/v1/awd/check-results");
        AuthSchemes("Internal");
        Policies("AwdCheckResult");
        RoutePrefixOverride(string.Empty);
        Summary(summary =>
        {
            summary.Summary = "Updates the status reported by an AWD checker.";
            summary.Description = "Records the result of one independently persisted AWD checker execution. Runtime and gameplay-fact identities come from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId) ||
            !Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"runtime:{runtimeId:D}",
                StringComparison.Ordinal) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            timeProvider.GetUtcNow() > DateTimeOffset.FromUnixTimeSeconds(deadline).AddHours(24))
            return TypedResults.Unauthorized();
        var disposition = await record.AwdAsync(AwdCheckResult.Create(
            runtimeId,
            gameplayFactId,
            InternalResultProtocolMapper.ToDomain(request.State),
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
            _ => TypedResults.Conflict()
        };
    }
}

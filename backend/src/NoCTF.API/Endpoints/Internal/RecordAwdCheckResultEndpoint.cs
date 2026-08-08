using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.Submissions.Processing;
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

public sealed class RecordAwdCheckResultEndpoint(RecordInternalResult record)
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
            summary.Description = "Each accepted update replaces the runtime's previous checker status. Runtime identity and generation come from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"runtime:{runtimeId:D}",
                StringComparison.Ordinal) ||
            !int.TryParse(User.FindFirstValue("generation"), out var generation) ||
            !long.TryParse(
                User.FindFirstValue("checker_sequence"),
                out var checkerSequence) ||
            !long.TryParse(
                User.FindFirstValue("processing_version"),
                out var processingVersion) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(deadline).AddHours(24))
            return TypedResults.Unauthorized();
        var disposition = await record.AwdAsync(AwdCheckResult.Create(
            runtimeId,
            generation,
            checkerSequence,
            processingVersion,
            InternalResultProtocolMapper.ToDomain(request.State),
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

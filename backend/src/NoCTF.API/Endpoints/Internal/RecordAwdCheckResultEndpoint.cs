using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions.Awd;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdCheckResultRequest
{
    public AwdCheckProbeRequest Connection { get; set; } = new();
    public AwdCheckProbeRequest Functionality { get; set; } = new();
    public List<AwdCheckFlagRequest> Flags { get; set; } = [];
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class AwdCheckProbeRequest
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdProbeOutcome>))]
    public AwdProbeOutcome Outcome { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdCheckReason>))]
    public AwdCheckReason Reason { get; set; }
    public AwdProbe ToDomain() => new(Outcome, Reason);
}

public sealed class AwdCheckFlagRequest
{
    public Guid FlagId { get; set; }
    public AwdCheckProbeRequest Write { get; set; } = new();
    public AwdCheckProbeRequest Read { get; set; } = new();
    public string? PrivateReadContext { get; set; }
    public string? PublicLocator { get; set; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<InternalResultDispositionProtocol>))]
public enum InternalResultDispositionProtocol
{
    Applied,
    Duplicate,
    Superseded,
    Conflict
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class InternalResultProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial InternalResultDispositionProtocol ToProtocol(
        InternalResultDisposition value);
}

public sealed class RecordAwdCheckResultValidator : Validator<RecordAwdCheckResultRequest>
{
    public RecordAwdCheckResultValidator()
    {
        RuleFor(request => request.ObservedAt).NotEmpty();
        RuleFor(request => request.Connection).NotNull();
        RuleFor(request => request.Functionality).NotNull();
        RuleFor(request => request.Flags).NotNull();
        When(request => request.Connection is not null, () =>
        {
            RuleFor(request => request.Connection.Outcome).IsInEnum();
            RuleFor(request => request.Connection.Reason).IsInEnum();
        });
        When(request => request.Functionality is not null, () =>
        {
            RuleFor(request => request.Functionality.Outcome).IsInEnum();
            RuleFor(request => request.Functionality.Reason).IsInEnum();
        });
        RuleForEach(request => request.Flags).NotNull().ChildRules(flag =>
        {
            flag.RuleFor(value => value.FlagId).NotEmpty();
            flag.RuleFor(value => value.Write).NotNull();
            flag.RuleFor(value => value.Read).NotNull();
            flag.RuleFor(value => value.PrivateReadContext).MaximumLength(8192);
            flag.RuleFor(value => value.PublicLocator).MaximumLength(2048);
            flag.When(value => value.Write is not null, () =>
            {
                flag.RuleFor(value => value.Write.Outcome).IsInEnum();
                flag.RuleFor(value => value.Write.Reason).IsInEnum();
            });
            flag.When(value => value.Read is not null, () =>
            {
                flag.RuleFor(value => value.Read.Outcome).IsInEnum();
                flag.RuleFor(value => value.Read.Reason).IsInEnum();
            });
        });
    }
}

public sealed record InternalResultResponse(
    InternalResultDispositionProtocol Disposition,
    string? Detail = null);

public sealed class RecordAwdCheckResultEndpoint(RecordInternalResult record, TimeProvider timeProvider)
    : Endpoint<RecordAwdCheckResultRequest,
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>>
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
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound,
            Conflict<InternalResultResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId) ||
            !Guid.TryParse(User.FindFirstValue("gameplay_fact_id"), out var gameplayFactId) ||
            !Guid.TryParse(User.FindFirstValue("attempt_id"), out var attemptId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"runtime:{runtimeId:D}",
                StringComparison.Ordinal) ||
            !long.TryParse(User.FindFirstValue("receipt_deadline"), out var receiptDeadline) ||
            timeProvider.GetUtcNow().UtcTicks - TimeSpan.FromHours(24).Ticks > receiptDeadline)
            return TypedResults.Unauthorized();
        var disposition = await record.AwdAsync(new AwdCheckResult(
            runtimeId,
            gameplayFactId,
            attemptId,
            request.Connection.ToDomain(), request.Functionality.ToDomain(),
            request.Flags.Select(flag => new AwdReportedFlag(flag.FlagId, flag.Write.ToDomain(), flag.Read.ToDomain(),
                flag.PrivateReadContext, flag.PublicLocator)).ToArray(), request.ObservedAt,
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
                    "The result conflicts with the runtime or gameplay fact currently bound to this checker.")),
            _ => throw new InvalidOperationException(
                $"Unsupported AWD result disposition: {disposition}.")
        };
    }
}

using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdpCheckResultRequest
{
    [JsonConverter(typeof(AwdpFixOutcomeJsonConverter))]
    public AwdpFixResultOutcome Outcome { get; set; }
}

public enum AwdpFixResultOutcome
{
    Fixed,
    StillVulnerable,
    RuleViolation,
    ServiceUnavailable
}

public sealed class AwdpFixOutcomeJsonConverter()
    : JsonStringEnumConverter<AwdpFixResultOutcome>(namingPolicy: null, allowIntegerValues: false);

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
            summary.Description = "SubmissionId and ProcessingVersion come exclusively from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdpCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("submission_id"), out var submissionId) ||
            !Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeInstanceId) ||
            !string.Equals(
                User.FindFirstValue("resource"),
                $"submission:{submissionId:D}:runtime:{runtimeInstanceId:D}",
                StringComparison.Ordinal) ||
            !int.TryParse(User.FindFirstValue("generation"), out var generation) ||
            !long.TryParse(User.FindFirstValue("processing_version"), out var processingVersion) ||
            !long.TryParse(
                User.FindFirstValue("runtime_processing_version"),
                out var runtimeProcessingVersion) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var disposition = await record.AwdpAsync(AwdpFixResult.Create(
            submissionId,
            runtimeInstanceId,
            generation,
            processingVersion,
            runtimeProcessingVersion,
            request.Outcome switch
            {
                AwdpFixResultOutcome.Fixed => AwdpFixOutcome.Fixed,
                AwdpFixResultOutcome.StillVulnerable => AwdpFixOutcome.StillVulnerable,
                AwdpFixResultOutcome.RuleViolation => AwdpFixOutcome.RuleViolation,
                AwdpFixResultOutcome.ServiceUnavailable => AwdpFixOutcome.ServiceUnavailable,
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Outcome, null)
            },
            DateTimeOffset.UtcNow), ct);
        return disposition switch
        {
            InternalResultDisposition.Applied or InternalResultDisposition.Duplicate =>
                TypedResults.Ok(new InternalResultResponse(disposition.ToString().ToLowerInvariant())),
            InternalResultDisposition.Superseded =>
                TypedResults.Accepted(
                    uri: (string?)null,
                    value: new InternalResultResponse("superseded")),
            InternalResultDisposition.NotFound => TypedResults.NotFound(),
            _ => TypedResults.Conflict()
        };
    }
}

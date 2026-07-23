using System.Security.Claims;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Runtime;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdCheckResultRequest
{
    [JsonConverter(typeof(AwdServiceStateJsonConverter))]
    public AwdServiceState State { get; set; }
}

public sealed class AwdServiceStateJsonConverter()
    : JsonStringEnumConverter<AwdServiceState>(namingPolicy: null, allowIntegerValues: false);

public sealed class RecordAwdCheckResultValidator : Validator<RecordAwdCheckResultRequest>
{
    public RecordAwdCheckResultValidator() =>
        RuleFor(request => request.State).IsInEnum();
}

public sealed record InternalResultResponse(string Disposition);

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
            summary.Summary = "Records a claim-bound AWD checker result.";
            summary.Description = "Runtime, generation, processing version, checker sequence, and deadline come exclusively from the internal JWT.";
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
            !long.TryParse(User.FindFirstValue("checker_sequence"), out var checkerSequence) ||
            !long.TryParse(User.FindFirstValue("processing_version"), out var processingVersion) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow > DateTimeOffset.FromUnixTimeSeconds(deadline).AddHours(24))
            return TypedResults.Unauthorized();
        var disposition = await record.AwdAsync(AwdCheckResult.Create(
            runtimeId,
            generation,
            checkerSequence,
            processingVersion,
            request.State,
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

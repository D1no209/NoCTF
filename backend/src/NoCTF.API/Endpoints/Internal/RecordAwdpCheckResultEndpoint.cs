using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdpCheckResultRequest
{
    public int ExitCode { get; set; }
    public bool TimedOut { get; set; }
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
            !long.TryParse(User.FindFirstValue("processing_version"), out var processingVersion) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{request.ExitCode}:{(request.TimedOut ? 1 : 0)}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var disposition = await record.AwdpAsync(new(
            submissionId,
            processingVersion,
            request.ExitCode,
            request.TimedOut,
            hash,
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

using System.Security.Claims;
using System.Security.Cryptography;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.API.Endpoints.Internal;

public sealed class RecordAwdCheckResultRequest
{
    public bool Up { get; set; }
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
            summary.Description = "Generation and CheckerSequence come exclusively from the internal JWT.";
        });
    }

    public override async Task<
        Results<Ok<InternalResultResponse>, Accepted<InternalResultResponse>, NotFound, Conflict, UnauthorizedHttpResult>> ExecuteAsync(
        RecordAwdCheckResultRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("runtime_instance_id"), out var runtimeId) ||
            !int.TryParse(User.FindFirstValue("generation"), out var generation) ||
            !long.TryParse(User.FindFirstValue("checker_sequence"), out var checkerSequence) ||
            !long.TryParse(User.FindFirstValue("deadline"), out var deadline) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() > deadline)
            return TypedResults.Unauthorized();
        var hash = SHA256.HashData([request.Up ? (byte)1 : (byte)0]);
        var disposition = await record.AwdAsync(new(
            runtimeId,
            generation,
            checkerSequence,
            request.Up,
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

using NoCTF.Application.Submissions.Events;
using Microsoft.AspNetCore.Mvc;

namespace NoCTF.API.Endpoints.Submissions;

public sealed class SubmitFlagRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Flag { get; set; } = string.Empty;
}

public sealed class SubmitFixRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed record AcceptedSubmissionResponse(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed class GetSubmissionStatusRequest
{
    public Guid CompetitionId { get; set; }
    public Guid SubmissionId { get; set; }
}

public sealed record SubmissionStatusResponse(
    Guid SubmissionId,
    string Kind,
    SubmissionOutcome Outcome,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode);

internal static class SubmissionProblemDetails
{
    public static async Task WriteAsync(HttpResponse response, int status, string? code, string? detail,
        CancellationToken cancellationToken)
    {
        response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Submission was not accepted.",
            Detail = detail,
            Type = "https://httpstatuses.com/" + status
        };
        if (!string.IsNullOrWhiteSpace(code))
            problem.Extensions["code"] = code;
        await response.WriteAsJsonAsync(problem, cancellationToken);
    }
}

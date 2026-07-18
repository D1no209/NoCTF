using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NoCTF.API.Endpoints.Submissions;

internal static class SubmissionProblemDetails
{
    public static int StatusFor(string? code) => code switch
    {
        "team_banned" or "team_forbidden" => StatusCodes.Status403Forbidden,
        "competition_finished" or "competition_not_started" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    public static ProblemHttpResult Create(int status, string? code, string? detail) =>
        TypedResults.Problem(
            statusCode: status,
            title: "Submission was not accepted.",
            detail: detail,
            type: "https://httpstatuses.com/" + status,
            extensions: string.IsNullOrWhiteSpace(code)
                ? null
                : new Dictionary<string, object?> { ["code"] = code });
}

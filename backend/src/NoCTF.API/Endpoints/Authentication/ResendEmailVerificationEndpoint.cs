using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class ResendEmailVerificationEndpoint(
    ResendEmailVerification resend,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/email-verification/resend");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Resend email verification";
            summary.Description = "Queues a new single-use email verification message.";
        });
    }

    public override async Task<
        Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await resend.ExecuteAsync(user.UserId, DateTimeOffset.UtcNow, ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Verification message was not queued.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode
                });
    }
}

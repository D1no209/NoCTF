using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.EmailVerification;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class SendEmailVerificationTestEndpoint(
    SendEmailVerificationTest sendTest,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/email-verification/test");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSendEmailVerificationTest"));
        Summary(summary =>
        {
            summary.Summary = "Tests SMTP delivery.";
            summary.Description = "Sends a test message to the current administrator.";
        });
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        try
        {
            var state = await sendTest.ExecuteAsync(user.UserId, ct);
            return state switch
            {
                EmailVerificationDeliveryState.Sent => TypedResults.NoContent(),
                EmailVerificationDeliveryState.RecipientNotFound => TypedResults.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Administrator account was not found."),
                EmailVerificationDeliveryState.Disabled => TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Email verification is disabled.",
                    detail: "Enable and save email verification before sending a test message.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = PlatformProblemCode.EmailVerificationDisabled
                    }),
                _ => TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "SMTP delivery is not configured.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = PlatformProblemCode.EmailDeliveryNotConfigured
                    })
            };
        }
        catch (EmailVerificationDeliveryException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "SMTP test delivery failed.",
                detail: "Check the SMTP host, credentials, encryption mode, and network access.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PlatformProblemCode.SmtpDeliveryFailed,
                    ["failure"] = exception.Failure.ToString()
                });
        }
    }
}

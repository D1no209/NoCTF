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
                EmailVerificationDeliveryState.RecipientNotFound => ApiProblems.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestTitleAdministratorAccountWasFound)),
                EmailVerificationDeliveryState.Disabled => ApiProblems.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestTitleEmailVerificationDisabled),
                    detail: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestDetailEnableSaveEmailVerification),
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = PlatformProblemCode.EmailVerificationDisabled
                    }),
                _ => ApiProblems.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestTitleSmtpDeliveryConfigured),
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = PlatformProblemCode.EmailDeliveryNotConfigured
                    })
            };
        }
        catch (EmailVerificationDeliveryException exception)
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestTitleSmtpTestDeliveryFailed),
                detail: ApiMessages.Get(ApiMessageId.SendEmailVerificationTestDetailCheckSmtpHostCredentials),
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PlatformProblemCode.SmtpDeliveryFailed,
                    ["failure"] = exception.Failure.ToString()
                });
        }
    }
}

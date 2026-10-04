using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class VerifyEmailValidator : Validator<VerifyEmailRequest>
{
    public VerifyEmailValidator() =>
        RuleFor(request => request.Token).NotEmpty().MaximumLength(128);
}

public sealed class VerifyEmailEndpoint(
    VerifyEmail verify,
    IEmailVerificationConfigurationStore configuration,
    TimeProvider timeProvider)
    : Endpoint<VerifyEmailRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/email-verification/verify");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Verify an email address";
            summary.Description = "Consumes a single-use email verification token.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(
        VerifyEmailRequest request,
        CancellationToken ct)
    {
        if (!(await configuration.GetAsync(ct)).Enabled)
            return TypedResults.NotFound();
        var result = await verify.ExecuteAsync(request.Token, timeProvider.GetUtcNow(), ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.VerifyEmailTitleEmailVerificationFailed),
                detail: ApiMessages.For(result.FailureCode));
    }
}

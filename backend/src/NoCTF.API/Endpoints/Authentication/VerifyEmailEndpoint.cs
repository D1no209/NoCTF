using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;

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

public sealed class VerifyEmailEndpoint(VerifyEmail verify, IConfiguration configuration)
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
        if (!configuration.GetValue("Authentication:EmailVerificationEnabled", false))
            return TypedResults.NotFound();
        var result = await verify.ExecuteAsync(request.Token, DateTimeOffset.UtcNow, ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Email verification failed.",
                detail: result.ErrorMessage);
    }
}

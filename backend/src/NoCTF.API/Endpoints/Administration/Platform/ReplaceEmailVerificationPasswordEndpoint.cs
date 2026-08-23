using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.EmailVerification;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ReplaceEmailVerificationPasswordRequest
{
    public required string Password { get; set; }
}

public sealed class ReplaceEmailVerificationPasswordValidator
    : Validator<ReplaceEmailVerificationPasswordRequest>
{
    public ReplaceEmailVerificationPasswordValidator()
    {
        RuleFor(request => request.Password).NotEmpty().MaximumLength(1024);
    }
}

public sealed class ReplaceEmailVerificationPasswordEndpoint(
    ManageEmailVerificationConfiguration configuration)
    : Endpoint<ReplaceEmailVerificationPasswordRequest,
        Results<Ok<EmailVerificationConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/email-verification/password");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformReplaceEmailVerificationPassword"));
        Summary(summary =>
        {
            summary.Summary = "Replaces the SMTP password.";
            summary.Description = "Replaces the encrypted SMTP password without returning it.";
        });
    }

    public override async Task<
        Results<Ok<EmailVerificationConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        ReplaceEmailVerificationPasswordRequest request,
        CancellationToken ct)
    {
        var result = await configuration.ReplacePasswordAsync(
            request.Password,
            DateTimeOffset.UtcNow,
            ct);
        if (result.State == EmailVerificationConfigurationUpdateState.Invalid)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "SMTP password is invalid.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = PlatformProblemCode.SmtpPasswordInvalid
                });
        }

        return TypedResults.Ok(EmailVerificationConfigurationMapping.ToResponse(
            result.Configuration!));
    }
}

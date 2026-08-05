using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.PasswordReset;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class CompletePasswordResetRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class CompletePasswordResetValidator : Validator<CompletePasswordResetRequest>
{
    public CompletePasswordResetValidator()
    {
        RuleFor(request => request.Token).NotEmpty().MaximumLength(128);
        RuleFor(request => request.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(1024);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CompletePasswordResetFailureCode>))]
public enum CompletePasswordResetFailureCode
{
    InvalidOrExpired
}

public sealed record CompletePasswordResetFailureResponse(
    CompletePasswordResetFailureCode Code);

public sealed class CompletePasswordResetEndpoint(CompletePasswordReset completePasswordReset)
    : Endpoint<CompletePasswordResetRequest,
        Results<NoContent, BadRequest<CompletePasswordResetFailureResponse>>>
{
    public override void Configure()
    {
        Post("/auth/password-reset/complete");
        AllowAnonymous();
        Description(builder => builder.WithName("AuthenticationCompletePasswordReset"));
        Summary(summary =>
        {
            summary.Summary = "Completes a password reset";
            summary.Description =
                "Consumes a single-use reset token, changes the password, and invalidates all sessions.";
        });
    }

    public override async Task<
        Results<NoContent, BadRequest<CompletePasswordResetFailureResponse>>> ExecuteAsync(
        CompletePasswordResetRequest request,
        CancellationToken ct)
    {
        var state = await completePasswordReset.ExecuteAsync(
            request.Token,
            request.NewPassword,
            DateTimeOffset.UtcNow,
            ct);
        if (state != PasswordResetCompletionState.Reset)
        {
            return TypedResults.BadRequest(new CompletePasswordResetFailureResponse(
                CompletePasswordResetFailureCode.InvalidOrExpired));
        }

        HttpContext.Response.Cookies.Delete(
            "__Secure-noctf_refresh",
            new CookieOptions { Path = "/api/v1/auth" });
        return TypedResults.NoContent();
    }
}

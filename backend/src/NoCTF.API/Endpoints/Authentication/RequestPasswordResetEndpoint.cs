using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.Application.Authentication.PasswordReset;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RequestPasswordResetRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed record RequestPasswordResetAcceptedResponse(bool Accepted);

public sealed class RequestPasswordResetValidator : Validator<RequestPasswordResetRequest>
{
    public RequestPasswordResetValidator() =>
        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);
}

public sealed class RequestPasswordResetEndpoint(
    RequestPasswordReset requestPasswordReset,
    TimeProvider timeProvider)
    : Endpoint<RequestPasswordResetRequest,
        Results<Accepted<RequestPasswordResetAcceptedResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/password-reset/request");
        AllowAnonymous();
        Options(builder => builder
            .WithMetadata(new EnableRateLimitingAttribute("password-reset-request")));
        Description(builder => builder
            .WithName("AuthenticationRequestPasswordReset")
            .Produces(StatusCodes.Status429TooManyRequests));
        Summary(summary =>
        {
            summary.Summary = "Requests a password reset email";
            summary.Description =
                "Always accepts a valid email-shaped request without revealing account existence.";
        });
    }

    public override async Task<
        Results<Accepted<RequestPasswordResetAcceptedResponse>, ProblemHttpResult>> ExecuteAsync(
        RequestPasswordResetRequest request,
        CancellationToken ct)
    {
        await requestPasswordReset.ExecuteAsync(request.Email, timeProvider.GetUtcNow(), ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new RequestPasswordResetAcceptedResponse(Accepted: true));
    }
}

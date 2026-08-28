using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RequestEmailVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed record RequestEmailVerificationAcceptedResponse(bool Accepted);

public sealed class RequestEmailVerificationValidator
    : Validator<RequestEmailVerificationRequest>
{
    public RequestEmailVerificationValidator() =>
        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);
}

public sealed class RequestEmailVerificationEndpoint(
    RequestEmailVerification requestEmailVerification,
    TimeProvider timeProvider)
    : Endpoint<RequestEmailVerificationRequest,
        Results<Accepted<RequestEmailVerificationAcceptedResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/email-verification/request");
        AllowAnonymous();
        Options(builder => builder.WithMetadata(
            new EnableRateLimitingAttribute("email-verification-request")));
        Description(builder => builder
            .WithName("AuthenticationRequestEmailVerification")
            .Produces(StatusCodes.Status429TooManyRequests));
        Summary(summary =>
        {
            summary.Summary = "Requests an email verification message";
            summary.Description =
                "Always accepts a valid email-shaped request without revealing account existence.";
        });
    }

    public override async Task<
        Results<Accepted<RequestEmailVerificationAcceptedResponse>, ProblemHttpResult>> ExecuteAsync(
        RequestEmailVerificationRequest request,
        CancellationToken ct)
    {
        await requestEmailVerification.ExecuteAsync(
            request.Email,
            timeProvider.GetUtcNow(),
            ct);
        return TypedResults.Accepted(
            uri: (string?)null,
            value: new RequestEmailVerificationAcceptedResponse(Accepted: true));
    }
}

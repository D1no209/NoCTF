using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class RegisterRequest
{
    private string userName = string.Empty;

    public string UserName
    {
        get => userName;
        set => userName = value?.Trim() ?? string.Empty;
    }
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed record RegisterResponse(
    Guid UserId,
    string UserName,
    string Email,
    UserRole Role,
    bool EmailVerified,
    bool RequiresEmailVerification,
    bool VerificationEmailQueued);

public sealed class RegisterValidator : Validator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(request => request.UserName)
            .NotEmpty().MinimumLength(3).MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$");
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty().MinimumLength(8).MaximumLength(1024);
    }
}

public sealed class RegisterEndpoint(RegisterUser register)
    : Endpoint<RegisterRequest, Results<Created<RegisterResponse>, Conflict<MvcProblemDetails>>>
{
    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
        Summary(summary =>
        {
            summary.Summary = "Register a user account";
            summary.Description = "Creates a local account with an Identity V3 password hash.";
        });
    }

    public override async Task<
        Results<Created<RegisterResponse>, Conflict<MvcProblemDetails>>> ExecuteAsync(
        RegisterRequest request,
        CancellationToken ct)
    {
        var result = await register.ExecuteAsync(
            new(request.UserName, request.Email, request.Password, DateTimeOffset.UtcNow),
            ct);
        if (!result.Succeeded)
            return TypedResults.Conflict(new MvcProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Account registration conflict.",
                Detail = result.ErrorMessage,
                Extensions = { ["code"] = result.ErrorCode }
            });

        var registration = result.Value!;
        var profile = registration.Profile;
        return TypedResults.Created(
            $"/api/v1/admin/platform/users/{profile.Id}",
            new RegisterResponse(
                profile.Id,
                profile.UserName,
                profile.Email,
                profile.Role,
                profile.EmailVerified,
                registration.RequiresEmailVerification,
                registration.VerificationEmailQueued));
    }
}

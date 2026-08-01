using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordValidator : Validator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(request => request.CurrentPassword).NotEmpty().MaximumLength(1024);
        RuleFor(request => request.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(1024);
        RuleFor(request => request.NewPassword)
            .NotEqual(request => request.CurrentPassword);
    }
}

public sealed class ChangePasswordEndpoint(ChangePassword change, IUserContext user)
    : Endpoint<ChangePasswordRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/auth/password");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Change the current password";
            summary.Description = "Rehashes the password and invalidates all existing tokens.";
        });
    }

    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(
        ChangePasswordRequest request,
        CancellationToken ct)
    {
        var result = await change.ExecuteAsync(
            user.UserId,
            request.CurrentPassword,
            request.NewPassword,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Password was not changed.",
                detail: result.ErrorMessage);
    }
}

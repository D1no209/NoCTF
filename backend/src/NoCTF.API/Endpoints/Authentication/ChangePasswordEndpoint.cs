using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using Microsoft.Extensions.Options;

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

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ChangePasswordFailureCode>))]
public enum ChangePasswordFailureCode
{
    CurrentPasswordInvalid
}

public sealed record ChangePasswordFailureResponse(
    ChangePasswordFailureCode Code,
    string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class ChangePasswordEndpoint(
    ChangePassword change,
    IUserContext user,
    IOptions<RefreshHttpOptions> options,
    TimeProvider timeProvider)
    : Endpoint<ChangePasswordRequest,
        Results<NoContent, Conflict<ChangePasswordFailureResponse>>>
{
    public override void Configure()
    {
        Put("/auth/password");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.Authentication)));
        MaxRequestBodySize(16 * 1024);
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Change the current password";
            summary.Description = "Rehashes the password and invalidates all existing tokens.";
        });
    }

    public override async Task<Results<NoContent, Conflict<ChangePasswordFailureResponse>>> ExecuteAsync(
        ChangePasswordRequest request,
        CancellationToken ct)
    {
        var result = await change.ExecuteAsync(
            user.UserId,
            request.CurrentPassword,
            request.NewPassword,
            timeProvider.GetUtcNow(),
            ct);
        if (result != ChangePasswordState.Changed)
            return TypedResults.Conflict(new ChangePasswordFailureResponse(
                ChangePasswordFailureCode.CurrentPasswordInvalid,
                "The current password is incorrect."));

        HttpContext.Response.Cookies.Delete(
            RefreshCookie.Name(options.Value),
            RefreshCookie.DeleteOptions(options.Value));
        return TypedResults.NoContent();
    }
}

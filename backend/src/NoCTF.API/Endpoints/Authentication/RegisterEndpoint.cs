using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Account;
using NoCTF.API.Serialization;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace NoCTF.API.Endpoints.Authentication;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RegisterFailureCode>))]
public enum RegisterFailureCode
{
    UserNameConflict,
    EmailConflict
}

[Mapper]
internal static partial class RegisterProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RegisterFailureCode ToProtocol(RegisterUserFailureCode value);
}

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
    UserRoleProtocol Role,
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

public sealed class RegisterEndpoint(RegisterUser register, TimeProvider timeProvider)
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
            new(request.UserName, request.Email, request.Password, timeProvider.GetUtcNow()),
            ct);
        if (!result.Succeeded)
            return TypedResults.Conflict(new MvcProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Account registration conflict.",
                Detail = result.ErrorMessage,
                Extensions =
                {
                    ["code"] = RegisterProtocolMapper.ToProtocol(result.FailureCode!.Value)
                }
            });

        var registration = result.Value!;
        var profile = registration.Profile;
        return TypedResults.Created(
            $"/api/v1/admin/platform/users/{profile.Id}",
            new RegisterResponse(
                profile.Id,
                profile.UserName,
                profile.Email,
                IdentityProtocolMapper.ToProtocol(profile.Role),
                profile.EmailVerified,
                registration.RequiresEmailVerification,
                registration.VerificationEmailQueued));
    }
}

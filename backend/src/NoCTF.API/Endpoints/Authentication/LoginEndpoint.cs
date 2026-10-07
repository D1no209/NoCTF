using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Login;
using NoCTF.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Authentication;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(AuthenticatedResponse), nameof(AuthenticationState.Authenticated))]
[JsonDerivedType(typeof(MfaRequiredResponse), nameof(AuthenticationState.MfaRequired))]
[JsonDerivedType(typeof(EnrollmentRequiredResponse), nameof(AuthenticationState.EnrollmentRequired))]
public abstract record AuthenticationResponse;

public sealed record AuthenticatedResponse(
    Guid UserId,
    string UserName,
    UserRoleProtocol Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string ReturnPath,
    IReadOnlyList<string>? RecoveryCodes = null) : AuthenticationResponse;
public sealed record MfaRequiredResponse(MfaFlowResponse Flow) : AuthenticationResponse;
public sealed record EnrollmentRequiredResponse(MfaFlowResponse Flow) : AuthenticationResponse;

internal static class AuthenticationResponseMapping
{
    public static AuthenticationResponse Map(AuthenticationCompletion completion, HttpContext context, RefreshHttpOptions refresh, MfaBrowserFlow browser)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (completion.State == AuthenticationState.Authenticated)
        {
            browser.Clear(context);
            context.Response.Cookies.Append(RefreshCookie.Name(refresh), completion.Refresh!.Token, RefreshCookie.Options(refresh));
            var user = completion.Authentication!.User;
            return new AuthenticatedResponse(user.Id, user.UserName, IdentityProtocolMapper.ToProtocol(user.Role), user.EmailVerified,
                completion.Access!.Token, completion.Access.ExpiresAt, completion.Authentication.ReturnPath, completion.Authentication.RecoveryCodes);
        }
        context.Response.Cookies.Delete(RefreshCookie.Name(refresh), RefreshCookie.DeleteOptions(refresh));
        browser.Write(context, completion.Browser!, completion.Flow!.ExpiresAt);
        return completion.State == AuthenticationState.MfaRequired ? new MfaRequiredResponse(MfaFlowResponse.From(completion.Flow))
            : new EnrollmentRequiredResponse(MfaFlowResponse.From(completion.Flow));
    }
}

public sealed class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginValidator : Validator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(request => request.Login).NotEmpty().MaximumLength(320);
        RuleFor(request => request.Password).NotEmpty().MaximumLength(1024);
    }
}

public sealed class LoginEndpoint(LoginUser login, IOptions<RefreshHttpOptions> options, MfaBrowserFlow browser)
    : Endpoint<LoginRequest, Results<Ok<AuthenticationResponse>, UnauthorizedHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.Authentication)));
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.HumanVerificationMetadata(HumanVerificationAction.Login)));
        MaxRequestBodySize(16 * 1024);
    }

    public override async Task<Results<Ok<AuthenticationResponse>, UnauthorizedHttpResult, ProblemHttpResult>> ExecuteAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await login.ExecuteAsync(new(request.Login, request.Password), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.FailureCode == LoginFailureCode.DependencyUnavailable) return MfaEndpointResults.Failure(MfaFailure.DependencyUnavailable);
            return TypedResults.Unauthorized();
        }
        return TypedResults.Ok(AuthenticationResponseMapping.Map(result.Value!, HttpContext, options.Value, browser));
    }
}

using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Security;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<EmailVerificationProblemCode>))]
internal enum EmailVerificationProblemCode
{
    EmailVerificationRequired
}

public sealed class EmailVerificationGateMiddleware(RequestDelegate next)
{
    private static readonly HashSet<PathString> AllowedPaths =
    [
        new("/api/v1/auth/me"),
        new("/api/v1/auth/me/profile"),
        new("/api/v1/auth/me/avatar"),
        new("/api/v1/auth/me/school-identity"),
        new("/api/v1/auth/refresh"),
        new("/api/v1/auth/logout"),
        new("/api/v1/auth/logout-all"),
        new("/api/v1/auth/password"),
        new("/api/v1/auth/email-verification/request"),
        new("/api/v1/auth/email-verification/resend"),
        new("/api/v1/auth/email-verification/verify")
    ];

    public async Task InvokeAsync(
        HttpContext context,
        IEmailVerificationConfigurationStore configuration,
        IUserAuthenticationStore users)
    {
        if (context.User.Identity?.IsAuthenticated != true
            || !string.Equals(
                context.User.FindFirst("user_kind")?.Value,
                "Human",
                StringComparison.Ordinal)
            || AllowedPaths.Contains(context.Request.Path))
        {
            await next(context);
            return;
        }

        var settings = await configuration.GetAsync(context.RequestAborted);
        if (!settings.Enabled)
        {
            await next(context);
            return;
        }

        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        var profile = Guid.TryParse(subject, out var userId)
            ? await users.GetProfileAsync(userId, context.RequestAborted)
            : null;
        if (profile?.EmailVerified == true)
        {
            await next(context);
            return;
        }

        await ApiProblems.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: ApiMessages.Get(ApiMessageId.EmailVerificationGateMiddlewareTitleEmailVerificationRequired),
            detail: ApiMessages.Get(ApiMessageId.EmailVerificationGateMiddlewareDetailVerifyAccountEmailAddress),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = EmailVerificationProblemCode.EmailVerificationRequired
            }).ExecuteAsync(context);
    }
}

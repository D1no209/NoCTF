using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed record CompleteSsoLoginResponse(
    Guid UserId,
    string UserName,
    UserRoleProtocol Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string ReturnPath);

public sealed class CompleteSsoLoginEndpoint(
    CompleteSsoLogin complete,
    SsoBrowserCorrelation correlation,
    IOptions<RefreshHttpOptions> refreshOptions)
    : EndpointWithoutRequest<
        Results<Ok<CompleteSsoLoginResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/sso/flows/{flowId:guid}/complete-login");
        AuthSchemes(AuthenticationRegistration.SsoFlowScheme);
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoCompleteLogin"));
        Summary(summary => { summary.Summary = "Consumes an authenticated SSO flow and issues a NoCTF session."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<CompleteSsoLoginResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var browserId = User.FindFirstValue(SsoFlowAuthenticationHandler.BrowserClaim);
        if (browserId is null)
            return SsoEndpointProblems.Create(SsoFailureCode.InvalidCorrelation);
        var result = await complete.ExecuteAsync(
            Route<Guid>("flowId"), correlation.Hash(browserId), ct);
        if (!result.Succeeded)
            return SsoEndpointProblems.Create(result.FailureCode!.Value);
        HttpContext.Response.Cookies.Append(
            RefreshCookie.Name(refreshOptions.Value),
            result.Value!.RefreshToken,
            RefreshCookie.Options(refreshOptions.Value));
        return TypedResults.Ok(new CompleteSsoLoginResponse(
            result.Value.UserId,
            result.Value.UserName,
            IdentityProtocolMapper.ToProtocol(result.Value.Role),
            result.Value.EmailVerified,
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt,
            result.Value.ReturnPath));
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class BeginSsoAuthenticationTestRequest
{
    public Guid ProviderId { get; set; }
}

public sealed class BeginSsoAuthenticationTestEndpoint(
    BeginSsoFlow begin,
    SsoBrowserCorrelation correlation)
    : Endpoint<BeginSsoAuthenticationTestRequest,
        Results<Ok<NoCTF.API.Endpoints.Authentication.BeginSsoLoginResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/sso/providers/{providerId:guid}/authentication-tests");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoBeginAuthenticationTest"));
        Summary(summary => summary.Summary = "Starts a browser authentication test without creating a binding or platform session.");
    }

    public override async Task<Results<Ok<NoCTF.API.Endpoints.Authentication.BeginSsoLoginResponse>,
        ProblemHttpResult>> ExecuteAsync(
        BeginSsoAuthenticationTestRequest request,
        CancellationToken ct)
    {
        var browserId = correlation.GetOrCreate(HttpContext);
        var result = await begin.ExecuteAsync(new(
            request.ProviderId,
            SsoFlowIntent.AdministratorTest,
            correlation.Hash(browserId),
            "/admin/platform/authentication"), ct);
        return result.Succeeded
            ? TypedResults.Ok(new NoCTF.API.Endpoints.Authentication.BeginSsoLoginResponse(
                result.Value!.FlowId,
                result.Value.AuthorizationUrl.AbsoluteUri,
                result.Value.ExpiresAt))
            : NoCTF.API.Endpoints.Authentication.SsoEndpointProblems.Create(
                result.FailureCode!.Value);
    }
}

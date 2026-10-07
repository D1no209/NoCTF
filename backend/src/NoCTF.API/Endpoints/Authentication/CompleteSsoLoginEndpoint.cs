using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class CompleteSsoLoginEndpoint(
    CompleteSsoLogin complete,
    SsoBrowserCorrelation correlation,
    IOptions<RefreshHttpOptions> refreshOptions,
    MfaBrowserFlow mfaBrowser)
    : EndpointWithoutRequest<
        Results<Ok<AuthenticationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/sso/flows/{flowId:guid}/complete-login");
        AuthSchemes(AuthenticationRegistration.SsoFlowScheme);
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoCompleteLogin"));
        Summary(summary => summary.Summary = "Consumes an authenticated SSO flow and issues a NoCTF session.");
    }

    public override async Task<Results<Ok<AuthenticationResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var browserId = User.FindFirstValue(SsoFlowAuthenticationHandler.BrowserClaim);
        if (browserId is null)
            return SsoEndpointProblems.Create(SsoFailureCode.InvalidCorrelation);
        var result = await complete.ExecuteAsync(
            Route<Guid>("flowId"), correlation.Hash(browserId), ct);
        if (!result.Succeeded)
            return SsoEndpointProblems.Create(result.FailureCode!.Value);
        return TypedResults.Ok(AuthenticationResponseMapping.Map(result.Value!.Completion, HttpContext, refreshOptions.Value, mfaBrowser));
    }
}

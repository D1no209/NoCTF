using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class OidcSsoCallbackRequest
{
    public Guid ProviderId { get; set; }
    public string? State { get; set; }
    public string? Code { get; set; }
    public string? Error { get; set; }
}

public sealed class OidcSsoCallbackEndpoint(
    CompleteSsoCallback complete,
    SsoBrowserCorrelation correlation)
    : Endpoint<OidcSsoCallbackRequest,
        Results<RedirectHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/sso/callback/oidc/{providerId:guid}");
        AllowAnonymous();
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoCallback)));
        Description(builder => builder.WithName("Authentication_SsoOidcCallback"));
        Summary(summary => { summary.Summary = "Consumes an OIDC authorization response once."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<RedirectHttpResult, ProblemHttpResult>> ExecuteAsync(
        OidcSsoCallbackRequest request,
        CancellationToken ct)
    {
        if (!SingleQueryValue("state")
            || HttpContext.Request.Query.TryGetValue("code", out var codes) && codes.Count != 1
            || HttpContext.Request.Query.TryGetValue("error", out var errors) && errors.Count != 1
            || string.IsNullOrWhiteSpace(request.State)
            || string.IsNullOrWhiteSpace(request.Code) == string.IsNullOrWhiteSpace(request.Error))
            return SsoEndpointProblems.Create(SsoFailureCode.AuthenticationFailed);
        var browserId = correlation.Read(HttpContext);
        if (browserId is null)
            return SsoEndpointProblems.Create(SsoFailureCode.InvalidCorrelation);
        var result = await complete.ExecuteAsync(
            request.ProviderId,
            SsoProtocol.Oidc,
            request.State!,
            correlation.Hash(browserId),
            request.Error is null ? request.Code! : string.Empty,
            ct);
        if (result.FlowId is null)
            return SsoEndpointProblems.Create(result.FailureCode ?? SsoFailureCode.InvalidCorrelation);
        return TypedResults.Redirect($"/auth/sso/complete?flow={result.FlowId.Value:N}");
    }

    private bool SingleQueryValue(string name) =>
        HttpContext.Request.Query.TryGetValue(name, out var values) && values.Count == 1;
}

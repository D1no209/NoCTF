using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class CasSsoCallbackRequest
{
    public Guid ProviderId { get; set; }
    public string? Flow { get; set; }
    public string? Ticket { get; set; }
}

public sealed class CasSsoCallbackEndpoint(
    CompleteSsoCallback complete,
    SsoBrowserCorrelation correlation)
    : Endpoint<CasSsoCallbackRequest,
        Results<RedirectHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/sso/callback/cas/{providerId:guid}");
        AllowAnonymous();
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoCallback)));
        Description(builder => builder.WithName("Authentication_SsoCasCallback"));
        Summary(summary => { summary.Summary = "Consumes a CAS service ticket once."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<RedirectHttpResult, ProblemHttpResult>> ExecuteAsync(
        CasSsoCallbackRequest request,
        CancellationToken ct)
    {
        if (!SingleQueryValue("flow")
            || !SingleQueryValue("ticket")
            || string.IsNullOrWhiteSpace(request.Flow)
            || string.IsNullOrWhiteSpace(request.Ticket))
            return SsoEndpointProblems.Create(SsoFailureCode.AuthenticationFailed);
        var browserId = correlation.Read(HttpContext);
        if (browserId is null)
            return SsoEndpointProblems.Create(SsoFailureCode.InvalidCorrelation);
        var result = await complete.ExecuteAsync(
            request.ProviderId,
            SsoProtocol.Cas,
            request.Flow,
            correlation.Hash(browserId),
            request.Ticket,
            ct);
        if (result.FlowId is null)
            return SsoEndpointProblems.Create(result.FailureCode ?? SsoFailureCode.InvalidCorrelation);
        return TypedResults.Redirect($"/auth/sso/complete?flow={result.FlowId.Value:N}");
    }

    private bool SingleQueryValue(string name) =>
        HttpContext.Request.Query.TryGetValue(name, out var values) && values.Count == 1;
}

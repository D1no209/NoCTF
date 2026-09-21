using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Sso;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Authentication;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SsoFlowStateProtocol>))]
public enum SsoFlowStateProtocol
{
    Pending,
    Processing,
    Authenticated,
    Failed,
    Consumed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SsoFlowIntentProtocol>))]
public enum SsoFlowIntentProtocol
{
    Login,
    Bind,
    AdministratorTest
}

public sealed class GetSsoFlowRequest
{
    public Guid FlowId { get; set; }
}

public sealed record SsoFlowStatusResponse(
    Guid FlowId,
    Guid ProviderId,
    SsoFlowStateProtocol State,
    SsoFlowIntentProtocol Intent,
    string? ProviderName,
    string? DisplayName,
    string? Subject,
    string? FailureCode,
    DateTimeOffset ExpiresAt);

public sealed class GetSsoFlowEndpoint(
    ISsoFlowStore flows,
    ManageSsoProviders providers,
    SsoBrowserCorrelation correlation)
    : Endpoint<GetSsoFlowRequest,
        Results<Ok<SsoFlowStatusResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/sso/flows/{flowId:guid}");
        AuthSchemes(AuthenticationRegistration.SsoFlowScheme);
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoGetFlow"));
        Summary(summary => summary.Summary = "Returns browser-bound SSO flow status and a safe identity summary.");
    }

    public override async Task<Results<Ok<SsoFlowStatusResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetSsoFlowRequest request,
        CancellationToken ct)
    {
        var browserId = User.FindFirstValue(SsoFlowAuthenticationHandler.BrowserClaim);
        if (browserId is null)
            return TypedResults.NotFound();
        var result = await flows.ReadAsync(request.FlowId, correlation.Hash(browserId), ct);
        if (result.State == SsoFlowReadState.DependencyUnavailable)
            return SsoEndpointProblems.Create(SsoFailureCode.ProviderUnavailable);
        if (result.State != SsoFlowReadState.Available || result.Flow is null)
            return TypedResults.NotFound();
        var flow = result.Flow;
        var configuration = await providers.GetAsync(ct);
        var providerName = configuration.Providers
            .SingleOrDefault(provider => provider.Id == flow.ProviderId)?.Name;
        return TypedResults.Ok(new SsoFlowStatusResponse(
            flow.Id,
            flow.ProviderId,
            (SsoFlowStateProtocol)flow.State,
            (SsoFlowIntentProtocol)flow.Intent,
            providerName,
            flow.ExternalIdentity?.DisplayName,
            flow.ExternalIdentity?.Subject,
            flow.FailureCode?.ToString(),
            flow.State is SsoFlowState.Authenticated or SsoFlowState.Failed
                ? flow.CreatedAt.Add(SsoRules.ResultLifetime)
                : flow.ExpiresAt));
    }
}

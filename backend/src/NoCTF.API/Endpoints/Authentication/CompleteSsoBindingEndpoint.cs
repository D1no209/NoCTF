using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class CompleteSsoBindingEndpoint(
    CompleteSsoBinding complete,
    SsoBrowserCorrelation correlation,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Ok<MySsoBindingResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/me/sso-binding/flows/{flowId:guid}/complete");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoCompleteBinding"));
        Summary(summary => summary.Summary = "Confirms and persists a browser-bound external identity binding.");
    }

    public override async Task<Results<Ok<MySsoBindingResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return SsoEndpointProblems.Create(SsoFailureCode.AccountUnavailable);
        var browserId = correlation.Read(HttpContext);
        if (browserId is null)
            return SsoEndpointProblems.Create(SsoFailureCode.InvalidCorrelation);
        var result = await complete.ExecuteAsync(
            Route<Guid>("flowId"),
            user.UserId,
            correlation.Hash(browserId),
            ct);
        if (!result.Succeeded)
            return SsoEndpointProblems.Create(result.FailureCode!.Value);
        var binding = result.Value!;
        return TypedResults.Ok(new MySsoBindingResponse(
            binding.ProviderId,
            binding.ProviderName,
            binding.ProviderIconUrl,
            binding.Protocol == NoCTF.Domain.Identity.SsoProtocol.Oidc
                ? PublicSsoProtocol.Oidc
                : PublicSsoProtocol.Cas,
            binding.IdentityNamespace,
            binding.Subject,
            binding.BoundAt));
    }
}

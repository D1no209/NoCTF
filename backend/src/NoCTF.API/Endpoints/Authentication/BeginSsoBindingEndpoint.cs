using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class BeginSsoBindingRequest
{
    public Guid ProviderId { get; set; }
}

public sealed class BeginSsoBindingValidator : Validator<BeginSsoBindingRequest>
{
    public BeginSsoBindingValidator()
    {
        RuleFor(request => request.ProviderId).NotEmpty();
    }
}

public sealed class BeginSsoBindingEndpoint(
    BeginSsoBinding begin,
    SsoBrowserCorrelation correlation,
    IUserContext user)
    : Endpoint<BeginSsoBindingRequest,
        Results<Ok<BeginSsoLoginResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/me/sso-binding/flows");
        AuthSchemes("Bearer");
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoBeginBinding"));
        Summary(summary => { summary.Summary = "Starts an external identity binding flow for the authenticated user."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<BeginSsoLoginResponse>, ProblemHttpResult>> ExecuteAsync(
        BeginSsoBindingRequest request,
        CancellationToken ct)
    {
        if (!user.IsHuman)
            return SsoEndpointProblems.Create(SsoFailureCode.AccountUnavailable);
        var browserId = correlation.GetOrCreate(HttpContext);
        var result = await begin.ExecuteAsync(
            user.UserId,
            request.ProviderId,
            correlation.Hash(browserId),
            ct);
        return result.Succeeded
            ? TypedResults.Ok(new BeginSsoLoginResponse(
                result.Value!.FlowId,
                result.Value.AuthorizationUrl.AbsoluteUri,
                result.Value.ExpiresAt))
            : SsoEndpointProblems.Create(result.FailureCode!.Value);
    }
}

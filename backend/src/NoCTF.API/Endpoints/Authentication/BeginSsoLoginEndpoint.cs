using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Authentication;

public sealed class BeginSsoLoginRequest
{
    public Guid ProviderId { get; set; }
    public string ReturnPath { get; set; } = "/";
}

public sealed class BeginSsoLoginValidator : Validator<BeginSsoLoginRequest>
{
    public BeginSsoLoginValidator()
    {
        RuleFor(request => request.ProviderId).NotEmpty();
        RuleFor(request => request.ReturnPath).NotEmpty().MaximumLength(2048);
    }
}

public sealed record BeginSsoLoginResponse(
    Guid FlowId,
    string AuthorizationUrl,
    DateTimeOffset ExpiresAt);

internal static class SsoEndpointProblems
{
    internal static ProblemHttpResult Create(SsoFailureCode code) =>
        TypedResults.Problem(
            statusCode: code switch
            {
                SsoFailureCode.ProviderNotFound => StatusCodes.Status404NotFound,
                SsoFailureCode.SsoDisabled or SsoFailureCode.ProviderUnavailable =>
                    StatusCodes.Status503ServiceUnavailable,
                SsoFailureCode.IdentityNotLinked
                    or SsoFailureCode.IdentityAlreadyLinked
                    or SsoFailureCode.AccountAlreadyLinked
                    or SsoFailureCode.ProviderChanged => StatusCodes.Status409Conflict,
                SsoFailureCode.FlowExpired => StatusCodes.Status410Gone,
                SsoFailureCode.AccountUnavailable => StatusCodes.Status403Forbidden,
                SsoFailureCode.ReauthenticationRequired => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest
            },
            title: "The SSO operation could not be completed.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code.ToString()
            });
}

public sealed class BeginSsoLoginEndpoint(
    BeginSsoFlow begin,
    SsoBrowserCorrelation correlation)
    : Endpoint<BeginSsoLoginRequest,
        Results<Ok<BeginSsoLoginResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/sso/login/flows");
        AllowAnonymous();
        Options(options => options.WithMetadata(
            new ProtectedEntryMetadata(ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("Authentication_SsoBeginLogin"));
        Summary(summary => summary.Summary = "Starts a browser-bound SSO login flow.");
    }

    public override async Task<Results<Ok<BeginSsoLoginResponse>, ProblemHttpResult>> ExecuteAsync(
        BeginSsoLoginRequest request,
        CancellationToken ct)
    {
        var browserId = correlation.GetOrCreate(HttpContext);
        var result = await begin.ExecuteAsync(new(
            request.ProviderId,
            SsoFlowIntent.Login,
            correlation.Hash(browserId),
            request.ReturnPath), ct);
        return result.Succeeded
            ? TypedResults.Ok(new BeginSsoLoginResponse(
                result.Value!.FlowId,
                result.Value.AuthorizationUrl.AbsoluteUri,
                result.Value.ExpiresAt))
            : SsoEndpointProblems.Create(result.FailureCode!.Value);
    }
}

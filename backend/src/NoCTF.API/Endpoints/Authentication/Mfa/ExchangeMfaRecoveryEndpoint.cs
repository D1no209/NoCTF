using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class ExchangeMfaRecoveryRequest { public string Token { get; set; } = string.Empty; }
public sealed class ExchangeMfaRecoveryValidator : Validator<ExchangeMfaRecoveryRequest>
{
    public ExchangeMfaRecoveryValidator() => RuleFor(value => value.Token).Length(43);
}
public sealed class ExchangeMfaRecoveryEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<ExchangeMfaRecoveryRequest, Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/recovery"); AllowAnonymous(); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(ExchangeMfaRecoveryRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var result = await management.ExchangeRecoveryAsync(request.Token, ct);
        if (!result.Succeeded) return MfaEndpointResults.Failure(result.FailureCode!.Value);
        browser.Write(HttpContext, result.Value!.Browser, result.Value.Flow.ExpiresAt);
        HttpContext.Response.Cookies.Delete(RefreshCookie.Name(options.Value), RefreshCookie.DeleteOptions(options.Value));
        return TypedResults.Ok(MfaFlowResponse.From(result.Value.Flow));
    }
}

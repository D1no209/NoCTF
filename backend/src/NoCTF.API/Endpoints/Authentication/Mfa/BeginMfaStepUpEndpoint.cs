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

public sealed class BeginMfaStepUpRequest
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaOperation>))] public MfaOperation Operation { get; set; }
    public Guid? TargetId { get; set; }
}
public sealed class BeginMfaStepUpValidator : Validator<BeginMfaStepUpRequest>
{
    public BeginMfaStepUpValidator() => RuleFor(value => value.Operation).IsInEnum();
}
public sealed class BeginMfaStepUpEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<BeginMfaStepUpRequest, Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/step-up"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(BeginMfaStepUpRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User);
        if (actor is null) return MfaEndpointResults.Failure(MfaFailure.NotApplicable);
        var result = await management.BeginStepUpAsync(actor, request.Operation, request.TargetId, ct);
        if (!result.Succeeded) return MfaEndpointResults.Failure(result.FailureCode!.Value);
        browser.Write(HttpContext, result.Value!.Browser, result.Value.Flow.ExpiresAt);
        return TypedResults.Ok(MfaFlowResponse.From(result.Value.Flow));
    }
}

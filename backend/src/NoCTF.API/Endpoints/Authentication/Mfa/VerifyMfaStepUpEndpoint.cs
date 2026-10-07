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

public sealed class VerifyMfaStepUpRequest
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaVerificationMethod>))] public MfaVerificationMethod Method { get; set; }
    public string Code { get; set; } = string.Empty;
}
public sealed class VerifyMfaStepUpValidator : Validator<VerifyMfaStepUpRequest>
{
    public VerifyMfaStepUpValidator() { RuleFor(value => value.Method).IsInEnum(); RuleFor(value => value.Code).NotEmpty().MaximumLength(64); }
}
public sealed class VerifyMfaStepUpEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<VerifyMfaStepUpRequest, Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/step-up/verify"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(VerifyMfaStepUpRequest request, CancellationToken ct)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var proof = browser.Read(HttpContext); if (proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.VerifyStepUpAsync(proof, new(request.Method, request.Code), ct);
        return result.Succeeded ? TypedResults.Ok(MfaFlowResponse.From(result.Value!)) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}

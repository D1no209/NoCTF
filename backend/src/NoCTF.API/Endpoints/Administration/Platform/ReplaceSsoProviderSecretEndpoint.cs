using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ReplaceSsoProviderSecretRequest
{
    public Guid ProviderId { get; set; }
    public required string Secret { get; set; }
}

public sealed class ReplaceSsoProviderSecretValidator : Validator<ReplaceSsoProviderSecretRequest>
{
    public ReplaceSsoProviderSecretValidator() =>
        RuleFor(request => request.Secret).NotEmpty().MaximumLength(SsoRules.MaximumSecretLength);
}

public sealed class ReplaceSsoProviderSecretEndpoint(
    ManageSsoProviders management,
    IUserContext user)
    : Endpoint<ReplaceSsoProviderSecretRequest,
        Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/sso/providers/{providerId:guid}/secret");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoReplaceProviderSecret"));
        Summary(summary => summary.Summary = "Replaces an OIDC client secret without returning it.");
    }

    public override async Task<Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        ReplaceSsoProviderSecretRequest request,
        CancellationToken ct)
    {
        var result = await management.ReplaceSecretAsync(
            request.ProviderId, request.Secret, user.UserId, ct);
        return result.State == SsoConfigurationMutationState.Updated
            ? TypedResults.Ok(SsoAdministrationMapping.ToResponse(result.Configuration!))
            : SsoAdministrationMapping.Problem(result.State);
    }
}

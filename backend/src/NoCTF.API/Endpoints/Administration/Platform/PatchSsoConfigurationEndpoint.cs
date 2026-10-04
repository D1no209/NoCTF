using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class PatchSsoConfigurationRequest
{
    public required bool Enabled { get; set; }
    public required string PublicBaseUrl { get; set; }
}

public sealed class PatchSsoConfigurationValidator : Validator<PatchSsoConfigurationRequest>
{
    public PatchSsoConfigurationValidator()
    {
        RuleFor(request => request.PublicBaseUrl).NotEmpty()
            .MaximumLength(SsoRules.MaximumUrlLength);
    }
}

public sealed class PatchSsoConfigurationEndpoint(
    ManageSsoProviders management,
    IUserContext user)
    : Endpoint<PatchSsoConfigurationRequest,
        Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/platform/sso");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoPatchConfiguration"));
        Summary(summary => { summary.Summary = "Updates the global SSO switch and public base URL."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchSsoConfigurationRequest request,
        CancellationToken ct)
    {
        var result = await management.UpdateGlobalAsync(
            request.Enabled, request.PublicBaseUrl, user.UserId, ct);
        return result.State == SsoConfigurationMutationState.Updated
            ? TypedResults.Ok(SsoAdministrationMapping.ToResponse(result.Configuration!))
            : SsoAdministrationMapping.Problem(result.State);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class PatchSsoConfigurationRequest
{
    public required bool Enabled { get; set; }
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
        Summary(summary => summary.Summary = "Updates the global SSO switch.");
    }

    public override async Task<Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        PatchSsoConfigurationRequest request,
        CancellationToken ct)
    {
        var result = await management.UpdateGlobalAsync(
            request.Enabled, user.UserId, ct);
        return result.State == SsoConfigurationMutationState.Updated
            ? TypedResults.Ok(SsoAdministrationMapping.ToResponse(result.Configuration!))
            : SsoAdministrationMapping.Problem(result.State);
    }
}

using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class UpdateSsoProviderRequest : SsoProviderWriteRequest
{
    public Guid ProviderId { get; set; }
}

public sealed class UpdateSsoProviderValidator : SsoProviderWriteValidator<UpdateSsoProviderRequest>;

public sealed class UpdateSsoProviderEndpoint(
    ManageSsoProviders management,
    IUserContext user)
    : Endpoint<UpdateSsoProviderRequest,
        Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/platform/sso/providers/{providerId:guid}");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoUpdateProvider"));
        Summary(summary => summary.Summary = "Updates a provider without exposing or replacing its secret.");
    }

    public override async Task<Results<Ok<SsoConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(
        UpdateSsoProviderRequest request,
        CancellationToken ct)
    {
        var result = await management.UpdateProviderAsync(
            request.ProviderId,
            SsoProviderRequestMapping.ToDraft(request),
            user.UserId,
            ct);
        return result.State == SsoConfigurationMutationState.Updated
            ? TypedResults.Ok(SsoAdministrationMapping.ToResponse(result.Configuration!))
            : SsoAdministrationMapping.Problem(result.State);
    }
}

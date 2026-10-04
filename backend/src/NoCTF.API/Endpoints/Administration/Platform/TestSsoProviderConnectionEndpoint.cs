using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record SsoProviderConnectionTestResponse(
    bool Succeeded,
    SsoProtocolProtocol? Protocol,
    string? Issuer,
    string? FailureStage,
    string? FailureCode);

public sealed class TestSsoProviderConnectionEndpoint(ManageSsoProviders management)
    : EndpointWithoutRequest<
        Results<Ok<SsoProviderConnectionTestResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/platform/sso/providers/{providerId:guid}/connection-tests");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Options(options => options.WithMetadata(
            new NoCTF.API.Security.ProtectedEntryMetadata(
                NoCTF.API.Security.ProtectedEntry.SsoAuthentication)));
        Description(builder => builder.WithName("AdminPlatformSsoTestProviderConnection"));
        Summary(summary => { summary.Summary = "Tests provider network, TLS and protocol metadata without authenticating a user."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<SsoProviderConnectionTestResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await management.TestConnectionAsync(Route<Guid>("providerId"), ct);
        if (result.FailureCode == "ProviderNotFound")
            return TypedResults.NotFound();
        return TypedResults.Ok(new SsoProviderConnectionTestResponse(
            result.Succeeded,
            result.Protocol is null
                ? null
                : SsoAdministrationMapping.ToProtocol(result.Protocol.Value),
            result.Issuer,
            result.FailureStage,
            result.FailureCode));
    }
}

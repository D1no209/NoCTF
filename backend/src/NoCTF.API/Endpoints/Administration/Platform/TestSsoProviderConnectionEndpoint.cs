using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Sso;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class TestSsoProviderConnectionRequest
{
    public Guid ProviderId { get; set; }
}

public sealed record SsoProviderConnectionTestResponse(
    bool Succeeded,
    SsoProtocolProtocol? Protocol,
    string? Issuer,
    string? FailureStage,
    string? FailureCode);

public sealed class TestSsoProviderConnectionEndpoint(ManageSsoProviders management)
    : Endpoint<TestSsoProviderConnectionRequest,
        Results<Ok<SsoProviderConnectionTestResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/platform/sso/providers/{providerId:guid}/connection-tests");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformSsoTestProviderConnection"));
        Summary(summary => summary.Summary = "Tests provider network, TLS and protocol metadata without authenticating a user.");
    }

    public override async Task<Results<Ok<SsoProviderConnectionTestResponse>, NotFound>> ExecuteAsync(
        TestSsoProviderConnectionRequest request,
        CancellationToken ct)
    {
        var result = await management.TestConnectionAsync(request.ProviderId, ct);
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

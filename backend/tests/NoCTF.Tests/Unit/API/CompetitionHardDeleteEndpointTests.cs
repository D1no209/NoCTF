using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Management;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

[NotInParallel]
public sealed class CompetitionHardDeleteEndpointTests
{
    private static readonly Guid ActorId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7390");
    private static readonly Guid CompetitionId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7391");
    private const string SigningKey =
        "competition-hard-delete-tests-use-a-stable-signing-key";

    [Test]
    public async Task Preview_and_blocked_delete_return_the_same_typed_impact()
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        var preview = new CompetitionHardDeletePreview(
            CompetitionId,
            "Protected competition",
            true,
            false,
            [
                new(
                    CompetitionHardDeleteReferenceKind.HistoricalEvent,
                    2)
            ]);
        store.PreviewHardDeleteAsync(
                CompetitionId,
                ActorId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(preview);
        store.HardDeleteAsync(
                CompetitionId,
                ActorId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionHardDeleteResult(
                CompetitionHardDeleteState.Blocked,
                preview));
        await using var app = await CreateApplicationAsync(store);
        using var client = app.GetTestClient();

        using var previewResponse = await client.GetAsync(PreviewUri());
        var previewBody = await previewResponse.Content
            .ReadFromJsonAsync<CompetitionHardDeletePreviewResponse>();
        using var deleteResponse = await client.DeleteAsync(DeleteUri());
        var deleteBody = await deleteResponse.Content
            .ReadFromJsonAsync<CompetitionHardDeletePreviewResponse>();

        await Assert.That(previewResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(previewBody).IsNotNull();
        await Assert.That(deleteBody).IsNotNull();
        await Assert.That(previewBody!.CanHardDelete).IsFalse();
        await Assert.That(previewBody.References).HasSingleItem();
        await Assert.That(previewBody.References[0].Code)
            .IsEqualTo(CompetitionHardDeleteReferenceCode.HistoricalEvent);
        await Assert.That(previewBody.References[0].Count).IsEqualTo(2);
        await Assert.That(deleteBody!.CompetitionId).IsEqualTo(previewBody.CompetitionId);
        await Assert.That(deleteBody.Title).IsEqualTo(previewBody.Title);
        await Assert.That(deleteBody.IsSoftDeleted).IsEqualTo(previewBody.IsSoftDeleted);
        await Assert.That(deleteBody.CanHardDelete).IsEqualTo(previewBody.CanHardDelete);
        await Assert.That(deleteBody.References).IsEquivalentTo(previewBody.References);
        await store.Received(1).PreviewHardDeleteAsync(
            CompetitionId,
            ActorId,
            false,
            Arg.Any<CancellationToken>());
        await store.Received(1).HardDeleteAsync(
            CompetitionId,
            ActorId,
            false,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Hard_delete_distinguishes_deleted_from_not_found()
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        store.HardDeleteAsync(
                CompetitionId,
                ActorId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(
                new CompetitionHardDeleteResult(CompetitionHardDeleteState.Deleted),
                new CompetitionHardDeleteResult(CompetitionHardDeleteState.NotFound));
        await using var app = await CreateApplicationAsync(store);
        using var client = app.GetTestClient();

        using var deleted = await client.DeleteAsync(DeleteUri());
        using var missing = await client.DeleteAsync(DeleteUri());

        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await store.Received(2).HardDeleteAsync(
            CompetitionId,
            ActorId,
            false,
            Arg.Any<CancellationToken>());
    }

    private static string PreviewUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/hard-delete-preview";

    private static string DeleteUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/hard-delete";

    private static async Task<WebApplication> CreateApplicationAsync(
        IAdminCompetitionStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(HardDeleteCompetitionEndpoint).Assembly];
            options.Filter = type => type == typeof(HardDeleteCompetitionEndpoint)
                || type == typeof(PreviewCompetitionHardDeleteEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(
                "Bearer",
                _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<HardDeleteCompetition>();
        builder.Services.AddScoped<PreviewCompetitionHardDelete>();
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class ActorUserContext : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => false;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

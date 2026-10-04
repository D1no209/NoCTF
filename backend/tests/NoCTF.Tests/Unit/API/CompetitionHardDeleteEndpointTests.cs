using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.OpenApi;
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
using NoCTF.API.Localization;
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

    [Test]
    public async Task Administrator_force_delete_sends_exact_confirmation_and_reason_once()
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        store.ForceDeleteAsync(
                Arg.Any<ForceDeleteCompetitionCommand>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionForceDeleteResult(CompetitionForceDeleteState.Deleted));
        await using var app = await CreateApplicationAsync(store, administrator: true);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            ForceDeleteUri(),
            new ForceDeleteCompetitionRequest
            {
                ConfirmationTitle = "Protected competition",
                Reason = "Remove this disposable competition fixture."
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await store.Received(1).ForceDeleteAsync(
            Arg.Is<ForceDeleteCompetitionCommand>(command =>
                command != null
                && command.CompetitionId == CompetitionId
                && command.ActorId == ActorId
                && command.ConfirmationTitle == "Protected competition"
                && command.Reason == "Remove this disposable competition fixture."),
            true,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Force_delete_returns_a_typed_live_runtime_conflict()
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        var preview = new CompetitionHardDeletePreview(
            CompetitionId,
            "Protected competition",
            false,
            false,
            false,
            [new(CompetitionHardDeleteReferenceKind.ActiveRuntimeResource, 1)]);
        store.ForceDeleteAsync(
                Arg.Any<ForceDeleteCompetitionCommand>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionForceDeleteResult(
                CompetitionForceDeleteState.ActiveRuntimeResource,
                preview));
        await using var app = await CreateApplicationAsync(store, administrator: true);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            ForceDeleteUri(),
            new ForceDeleteCompetitionRequest
            {
                ConfirmationTitle = "Protected competition",
                Reason = "A sufficiently detailed deletion reason."
            });
        var body = await response.Content
            .ReadFromJsonAsync<CompetitionForceDeleteConflictResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Code)
            .IsEqualTo(CompetitionForceDeleteConflictCode.ActiveRuntimeResource);
        await Assert.That(body.Preview!.References).HasSingleItem();
        await Assert.That(body.Preview.References[0].Code)
            .IsEqualTo(CompetitionHardDeleteReferenceCode.ActiveRuntimeResource);
    }

    [Test]
    [Arguments(CompetitionForceDeleteState.ActiveCompetition)]
    [Arguments(CompetitionForceDeleteState.NotificationScopeConflict)]
    public async Task Force_delete_conflicts_explain_the_required_action(CompetitionForceDeleteState state)
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        var notificationId = Guid.NewGuid();
        store.ForceDeleteAsync(Arg.Any<ForceDeleteCompetitionCommand>(), true, Arg.Any<CancellationToken>())
            .Returns(new CompetitionForceDeleteResult(state, ConflictingNotificationIds: [notificationId]));
        await using var app = await CreateApplicationAsync(store, administrator: true);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(ForceDeleteUri(), new ForceDeleteCompetitionRequest
        {
            ConfirmationTitle = "Protected competition", Reason = "Detailed deletion test reason."
        });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement.Deserialize<CompetitionForceDeleteConflictResponse>(JsonSerializerOptions.Web);
        var detail = document.RootElement.GetProperty("detail").GetString();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        if (state == CompetitionForceDeleteState.ActiveCompetition)
            await Assert.That(detail).IsEqualTo("Finish the competition before permanently deleting it. Paused competitions cannot be deleted.");
        else
        {
            await Assert.That(body!.Code).IsEqualTo(CompetitionForceDeleteConflictCode.NotificationScopeConflict);
            await Assert.That(body.ConflictingNotificationIds).IsEquivalentTo([notificationId]);
            await Assert.That(detail).Contains("No data was deleted");
        }
    }

    [Test]
    [Arguments(false, "Detailed deletion test reason.", HttpStatusCode.Forbidden)]
    [Arguments(true, "short", HttpStatusCode.BadRequest)]
    public async Task Force_delete_rejects_unauthorized_and_invalid_requests_before_store(
        bool administrator, string reason, HttpStatusCode expected)
    {
        var store = Substitute.For<IAdminCompetitionStore>();
        await using var app = await CreateApplicationAsync(store, administrator);
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(ForceDeleteUri(), new ForceDeleteCompetitionRequest
        {
            ConfirmationTitle = "Protected competition", Reason = reason
        });
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await store.DidNotReceive().ForceDeleteAsync(Arg.Any<ForceDeleteCompetitionCommand>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private static string PreviewUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/hard-delete-preview";

    private static string DeleteUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/hard-delete";

    private static string ForceDeleteUri() =>
        $"/api/v1/admin/competitions/{CompetitionId}/force-delete";

    private static async Task<WebApplication> CreateApplicationAsync(
        IAdminCompetitionStore store,
        bool administrator = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddNoCtfLocalization();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(HardDeleteCompetitionEndpoint).Assembly];
            options.Filter = type => type == typeof(HardDeleteCompetitionEndpoint)
                || type == typeof(PreviewCompetitionHardDeleteEndpoint)
                || type == typeof(ForceDeleteCompetitionEndpoint);
        });
        builder.Services.OpenApiDocument();
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
        builder.Services.AddScoped<ForceDeleteCompetition>();
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext(administrator));
        builder.Services.AddSingleton(new TestIdentityOptions(administrator));

        var app = builder.Build();
        app.UseRequestLocalization();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class ActorUserContext(bool administrator) : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => administrator;
    }

    private sealed record TestIdentityOptions(bool Administrator);

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestIdentityOptions identityOptions)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, ActorId.ToString())
            };
            if (identityOptions.Administrator)
                claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

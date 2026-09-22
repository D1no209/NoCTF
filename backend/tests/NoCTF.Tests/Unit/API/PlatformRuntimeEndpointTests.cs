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
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Endpoints.Administration.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.API.Pagination;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.API;

public sealed class PlatformRuntimeEndpointTests
{
    private const string Route = "/api/v1/admin/platform/runtimes";

    [Test, Arguments(true), Arguments(false)]
    public async Task Allocation_amounts_are_disclosed_only_to_platform_administrators(bool administrator)
    {
        var id = Guid.NewGuid();
        var store = CreateStore();
        var view = new RuntimeInstanceView(id, Guid.NewGuid(), Guid.NewGuid(), null, null, RuntimePurpose.Player,
            RuntimeKind.Container, RuntimeProvider.Docker, RuntimeState.Running, null, DateTimeOffset.UtcNow, null, null, null)
        {
            Capacity = RuntimeCapacityAllocations.Empty.Add(new(new(RuntimeWorkloadKind.Runtime, id, id), null,
                "domain", "runner", new(1024, 250, 128), new(1024, 500, 128)))
        };
        store.FindPlatformAsync(id, Arg.Any<CancellationToken>()).Returns(view);
        await using var app = await CreateApp(store, administrator);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", administrator ? "Administrator" : "Organizer");
        var result = await client.GetFromJsonAsync<AdminRuntimeResponse>($"/api/v1/admin/runtimes/{id}");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Capacity is not null).IsEqualTo(administrator);
        if (administrator)
            await Assert.That(result.Capacity!.Single().Budget.NanoCpus).IsEqualTo(250);
    }

    [Test]
    [Arguments("Administrator", HttpStatusCode.OK)]
    [Arguments("User", HttpStatusCode.Forbidden)]
    [Arguments("", HttpStatusCode.Unauthorized)]
    public async Task Inventory_requires_platform_administrator(string role, HttpStatusCode expected)
    {
        var store = CreateStore();
        await using var app = await CreateApp(store);
        using var client = app.GetTestClient();
        if (role.Length > 0) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync(Route);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(store.ReceivedCalls().Count()).IsEqualTo(expected == HttpStatusCode.OK ? 1 : 0);
    }

    [Test]
    public async Task Filters_and_offset_page_reach_the_store()
    {
        var store = CreateStore();
        await using var app = await CreateApp(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        const string filters = "search=soul&scope=Competition&state=Running&runtimeKind=Container&offset=5&limit=1&desc=true";
        var page = await client.GetFromJsonAsync<PlatformRuntimeListResponse>($"{Route}?{filters}");
        await Assert.That(page!.Total).IsEqualTo(1);
        await store.Received(1).ListActiveContainersPageAsync(
            new("soul", PlatformRuntimeScope.Competition, RuntimeState.Running, RuntimeKind.Container),
            5, 1, true, Arg.Any<CancellationToken>());
        await Assert.That(store.ReceivedCalls()).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("state=Stopped")]
    [Arguments("runtimeKind=OvaVm")]
    [Arguments("scope=999")]
    [Arguments("limit=201")]
    public async Task Invalid_inventory_filters_return_validation_errors(string query)
    {
        var store = CreateStore();
        await using var app = await CreateApp(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        using var response = await client.GetAsync($"{Route}?{query}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(store.ReceivedCalls()).IsEmpty();
    }

    private static IAdminRuntimeStore CreateStore()
    {
        var store = Substitute.For<IAdminRuntimeStore>();
        store.ListActiveContainersAsync(Arg.Any<PlatformRuntimeFilter>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<IReadOnlyList<PlatformRuntimeInstanceView>>([
                new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, RuntimePurpose.Player,
                    RuntimeKind.Container, RuntimeProvider.Docker, RuntimeState.Running, null,
                    DateTimeOffset.UtcNow, null, null, null), PlatformRuntimeScope.Competition, "Contest", "soul")
            ]));
        store.ListActiveContainersPageAsync(
                Arg.Any<PlatformRuntimeFilter>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlatformRuntimeListPage(
                [new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, RuntimePurpose.Player,
                    RuntimeKind.Container, RuntimeProvider.Docker, RuntimeState.Running, null,
                    DateTimeOffset.UtcNow, null, null, null), PlatformRuntimeScope.Competition, "Contest", "soul")],
                1)));
        return store;
    }

    private static async Task<WebApplication> CreateApp(IAdminRuntimeStore store, bool administrator = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListPlatformRuntimesEndpoint).Assembly];
            options.Filter = type => type == typeof(ListPlatformRuntimesEndpoint)
                || type == typeof(ListPlatformRuntimesValidator) || type == typeof(GetAdminRuntimeEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        var user = Substitute.For<IUserContext>();
        user.IsAdministrator.Returns(administrator);
        builder.Services.AddSingleton(user);
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanObserveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<ManageAdminRuntimes>();
        builder.Services.AddSingleton(TimeProvider.System);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestBearer(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
                    Scheme.Name)), Scheme.Name)));
        }
    }
}

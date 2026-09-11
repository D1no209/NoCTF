using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class CreateRuntimeEndpointTests
{
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid CompetitionChallengeId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Test]
    [Arguments(RuntimeState.Stopped)]
    [Arguments(RuntimeState.Failed)]
    public async Task Start_accepts_a_terminal_historical_runtime(RuntimeState state)
    {
        var store = Substitute.For<IRuntimeInstanceStore>();
        store.FindPlayerRuntimeAsync(
                CompetitionId,
                CompetitionChallengeId,
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(Runtime(Guid.CreateVersion7(), state));
        store.MutatePlayerRuntimeAsync(
                Arg.Any<RuntimeMutationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new RuntimeMutationResult(
                Runtime(Guid.CreateVersion7(), RuntimeState.Queued)));
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/v1/competitions/{CompetitionId}/challenges/{CompetitionChallengeId}/runtimes",
            new { replacesRuntimeId = (Guid?)null });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await store.Received(1).MutatePlayerRuntimeAsync(
            Arg.Is<RuntimeMutationCommand>(command =>
                command != null && command.Action == RuntimeAction.Start),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(RuntimeState.Queued)]
    [Arguments(RuntimeState.Provisioning)]
    [Arguments(RuntimeState.Running)]
    [Arguments(RuntimeState.Stopping)]
    public async Task Start_without_a_replacement_rejects_an_active_runtime(RuntimeState state)
    {
        var store = Substitute.For<IRuntimeInstanceStore>();
        store.FindPlayerRuntimeAsync(
                CompetitionId,
                CompetitionChallengeId,
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(Runtime(Guid.CreateVersion7(), state));
        await using var app = await CreateApplicationAsync(store);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            $"/api/v1/competitions/{CompetitionId}/challenges/{CompetitionChallengeId}/runtimes",
            new { replacesRuntimeId = (Guid?)null });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await store.DidNotReceiveWithAnyArgs()
            .MutatePlayerRuntimeAsync(default!, default);
    }

    private static RuntimeInstanceView Runtime(Guid id, RuntimeState state) =>
        new(
            id,
            CompetitionId,
            CompetitionChallengeId,
            null,
            Guid.CreateVersion7(),
            RuntimePurpose.Player,
            RuntimeKind.Container,
            RuntimeProvider.Docker,
            state,
            null,
            [],
            DateTimeOffset.UtcNow,
            null,
            null,
            state == RuntimeState.Stopped ? DateTimeOffset.UtcNow : null);

    private static async Task<WebApplication> CreateApplicationAsync(
        IRuntimeInstanceStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(CreateRuntimeEndpoint).Assembly];
            options.Filter = type => type == typeof(CreateRuntimeEndpoint);
        });
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
        builder.Services.AddScoped<GetPlayerRuntime>();
        builder.Services.AddScoped<MutatePlayerRuntime>();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext());
        builder.Services.AddSingleton(TimeProvider.System);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestUserContext : IUserContext
    {
        public Guid UserId => CreateRuntimeEndpointTests.UserId;
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
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

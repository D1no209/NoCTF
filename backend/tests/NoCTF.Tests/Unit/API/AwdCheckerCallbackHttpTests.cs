using System.Net;
using System.Net.Http.Headers;
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
using NoCTF.API.Endpoints.Internal;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Competitions.Awd;

namespace NoCTF.Tests.Unit.API;

public sealed class AwdCheckerCallbackHttpTests
{
    private const string InternalScheme = "Internal";

    [Test]
    public async Task Task_material_uses_only_token_scope_and_disables_caching()
    {
        var runtimeId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var reader = new CapturingTaskReader();
        await using var app = await CreateApplicationAsync(Claims(runtimeId, factId.ToString("D")), new CapturingInternalResultStore(), reader);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(InternalScheme, "test-token");
        using var response = await client.GetAsync($"/api/internal/v1/awd/check-task?gameplayFactId={Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(reader.RuntimeId).IsEqualTo(runtimeId);
        await Assert.That(reader.FactId).IsEqualTo(factId);
        var body = await response.Content.ReadFromJsonAsync<AwdCheckTaskResponse>();
        await Assert.That(body!.GameplayFactId).IsEqualTo(factId);
    }

    [Test]
    public async Task Claims_are_forwarded_to_the_persistence_fence()
    {
        var runtimeId = Guid.Parse("0f66c20e-6064-4a20-a54e-bbe6a79aff56");
        var gameplayFactId = Guid.Parse("1f66c20e-6064-4a20-a54e-bbe6a79aff56");
        var store = new CapturingInternalResultStore();
        await using var app = await CreateApplicationAsync(
            Claims(runtimeId, gameplayFactId.ToString("D")),
            store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(InternalScheme, "test-token");

        using var response = await client.PostAsJsonAsync(
            "/api/internal/v1/awd/check-results",
            new { connection = new { outcome = "TargetFailure", reason = "ConnectionRefused" }, functionality = new { outcome = "Unknown", reason = "None" }, flags = Array.Empty<object>(), observedAt = DateTimeOffset.UtcNow });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(store.AwdCalls).IsEqualTo(1);
        await Assert.That(store.AwdResult).IsNotNull();
        await Assert.That(store.AwdResult!.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(store.AwdResult.GameplayFactId).IsEqualTo(gameplayFactId);
    }

    [Test]
    public async Task Malformed_fence_claim_is_rejected_before_persistence()
    {
        var runtimeId = Guid.Parse("0f66c20e-6064-4a20-a54e-bbe6a79aff56");
        var store = new CapturingInternalResultStore();
        await using var app = await CreateApplicationAsync(
            Claims(runtimeId, gameplayFactId: "not-a-guid"),
            store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(InternalScheme, "test-token");

        using var response = await client.PostAsJsonAsync(
            "/api/internal/v1/awd/check-results",
            new { connection = new { outcome = "TargetFailure", reason = "ConnectionRefused" }, functionality = new { outcome = "Unknown", reason = "None" }, flags = Array.Empty<object>(), observedAt = DateTimeOffset.UtcNow });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(store.AwdCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(InternalResultDisposition.Superseded, HttpStatusCode.Accepted)]
    [Arguments(InternalResultDisposition.Conflict, HttpStatusCode.Conflict)]
    public async Task Fence_disposition_maps_to_the_typed_http_status(
        InternalResultDisposition disposition,
        HttpStatusCode expectedStatus)
    {
        var runtimeId = Guid.Parse("0f66c20e-6064-4a20-a54e-bbe6a79aff56");
        var store = new CapturingInternalResultStore
        {
            Disposition = disposition
        };
        await using var app = await CreateApplicationAsync(
            Claims(runtimeId, Guid.NewGuid().ToString("D")),
            store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(InternalScheme, "test-token");

        using var response = await client.PostAsJsonAsync(
            "/api/internal/v1/awd/check-results",
            new { connection = new { outcome = "TargetFailure", reason = "ConnectionRefused" }, functionality = new { outcome = "Unknown", reason = "None" }, flags = Array.Empty<object>(), observedAt = DateTimeOffset.UtcNow });

        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(store.AwdCalls).IsEqualTo(1);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        IReadOnlyList<Claim> claims,
        IInternalResultStore store,
        IAwdCheckerTaskReader? tasks = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(RecordAwdCheckResultEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(RecordAwdCheckResultEndpoint)
                || type == typeof(ReadAwdCheckTaskEndpoint)
                || type == typeof(RecordAwdCheckResultValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddSingleton(new TestClaims(claims));
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = InternalScheme;
                options.DefaultChallengeScheme = InternalScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestInternalHandler>(
                InternalScheme,
                _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AwdCheckTask", policy => policy.AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser().RequireClaim("permission", "awd:check-task:read"));
            options.AddPolicy("AwdCheckResult", policy => policy
                .AddAuthenticationSchemes(InternalScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("token_type", "internal")
                .RequireClaim("permission", "awd:check-result:write")
                .RequireClaim("resource")
                .RequireClaim("runtime_instance_id")
                .RequireClaim("gameplay_fact_id")
                .RequireClaim("deadline"));
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(tasks ?? new CapturingTaskReader());
        builder.Services.AddScoped<RecordInternalResult>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private static IReadOnlyList<Claim> Claims(
        Guid runtimeId,
        string gameplayFactId) =>
    [
        new("token_type", "internal"),
        new("permission", "awd:check-result:write"),
        new("permission", "awd:check-task:read"),
        new("resource", $"runtime:{runtimeId:D}"),
        new("runtime_instance_id", runtimeId.ToString("D")),
        new("gameplay_fact_id", gameplayFactId), new("attempt_id", Guid.NewGuid().ToString("D")), new("receipt_deadline", DateTimeOffset.UtcNow.AddMinutes(5).UtcTicks.ToString()),
        new(
            "deadline",
            DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString())
    ];

    private sealed record TestClaims(IReadOnlyList<Claim> Values);

    private sealed class CapturingTaskReader : IAwdCheckerTaskReader
    {
        public Guid RuntimeId { get; private set; }
        public Guid FactId { get; private set; }
        public Task<AwdCheckerTask?> ReadAsync(Guid runtimeId, Guid factId, Guid attemptId, DateTimeOffset at, CancellationToken ct)
        {
            RuntimeId = runtimeId;
            FactId = factId;
            return Task.FromResult<AwdCheckerTask?>(new(factId, attemptId, at.AddSeconds(30), []));
        }
    }

    private sealed class TestInternalHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestClaims claims)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(claims.Values, Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class CapturingInternalResultStore : IInternalResultStore
    {
        public int AwdCalls { get; private set; }
        public AwdCheckResult? AwdResult { get; private set; }
        public InternalResultDisposition Disposition { get; init; } =
            InternalResultDisposition.Applied;

        public Task<InternalResultDisposition> RecordAwdAsync(
            AwdCheckResult result,
            CancellationToken cancellationToken)
        {
            AwdCalls++;
            AwdResult = result;
            return Task.FromResult(Disposition);
        }

        public Task<InternalResultDisposition> RecordAwdpAsync(
            AwdpFixResult result,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.Storage;

namespace NoCTF.Tests.Unit.API;

public sealed class UploadPatchEndpointTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid CompetitionId = Guid.CreateVersion7();
    private static readonly Guid CompetitionChallengeId = Guid.CreateVersion7();
    private static readonly Guid RuntimeInstanceId = Guid.CreateVersion7();

    [Test]
    public async Task Configured_patch_limit_returns_typed_413_before_storage()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2]), "file", "fix.tar.gz");

        using var response = await client.PostAsync(
            $"/api/v1/competitions/{CompetitionId}/challenges/{CompetitionChallengeId}/awdp-defense-targets/{RuntimeInstanceId}/fix",
            form);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]?.ToString())
            .Contains(PatchUploadFailureCode.ArchiveTooLarge.ToString());
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(UploadPatchEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(UploadPatchEndpoint)
                || type == typeof(UploadPatchValidator);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext());
        builder.Services.AddSingleton<IPatchUploadStore>(new LimitedPatchStore());
        builder.Services.AddSingleton<IStore, FailingStore>();
        builder.Services.AddSingleton<IManagedFileUploadRegistry, FailingUploadRegistry>();
        builder.Services.AddScoped<ManagedFileUploads>();
        builder.Services.AddScoped<CreatePatchUpload>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class LimitedPatchStore : IPatchUploadStore
    {
        public Task<PatchUploadScope?> ResolveScopeAsync(
            Guid competitionId,
            Guid competitionChallengeId,
            Guid runtimeInstanceId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PatchUploadScope?>(new(
                competitionId,
                competitionChallengeId,
                Guid.CreateVersion7(),
                userId,
                runtimeInstanceId,
                MaximumArchiveBytes: 1));

        public Task<PatchUploadSaveResult> SaveAsync(
            Guid patchUploadId,
            Guid gameplayFactId,
            PatchUploadScope scope,
            Guid fileId,
            DateTimeOffset uploadedAt,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Storage must not be reached.");
    }

    private sealed class FailingStore : StoreBase
    {
        public override Task SetObject(
            string objectKey,
            Stream content,
            string contentType,
            bool append = false,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage must not be reached.");
    }

    private sealed class FailingUploadRegistry : IManagedFileUploadRegistry
    {
        public Task RegisterAsync(ManagedFileUpload file, DateTimeOffset createdAt,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Storage must not be reached.");
        public Task AbandonAsync(Guid fileId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Storage must not be reached.");
    }

    private sealed class TestUserContext : IUserContext
    {
        public Guid UserId => UploadPatchEndpointTests.UserId;
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

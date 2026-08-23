using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Endpoints;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using Wolverine;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Tests.Integration.API;

public sealed class DevelopmentHostingTests
{
    [Test]
    [Category("DevelopmentIntegration")]
    public async Task Development_host_uses_single_process_in_memory_infrastructure(
        CancellationToken cancellationToken)
    {
        using var factory = new WebApplicationFactory<HealthEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("SeedAdmin:UserName", "integration-admin");
                builder.UseSetting("SeedAdmin:Email", "integration-admin@noctf.local");
                builder.UseSetting("SeedAdmin:Password", "integration-password");
            });
        using var client = factory.CreateClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            await Assert.That(db.Database.ProviderName)
                .IsEqualTo("Microsoft.EntityFrameworkCore.InMemory");
            var caches = scope.ServiceProvider.GetRequiredService<IFusionCacheProvider>();
            await Assert.That(caches.GetCache(NoCtfCacheNames.Leaderboards)).IsNotNull();
            await Assert.That(caches.GetCache(NoCtfCacheNames.ReadModels)).IsNotNull();
            await Assert.That(caches.GetCache(NoCtfCacheNames.LocalComputation)).IsNotNull();
            await Assert.That(scope.ServiceProvider
                    .GetRequiredService<IChallengeRuntimeTemplateCatalog>())
                .IsTypeOf<FusionChallengeRuntimeTemplateCatalog>();
            await Assert.That(scope.ServiceProvider
                    .GetRequiredService<IAwdRoundConfigurationCatalog>())
                .IsTypeOf<FusionAwdRoundConfigurationCatalog>();
            await Assert.That(scope.ServiceProvider
                    .GetRequiredService<IAwdFlagInjectionConfigurationCatalog>())
                .IsTypeOf<FusionAwdFlagInjectionConfigurationCatalog>();
            await Assert.That(scope.ServiceProvider
                    .GetRequiredService<IKohProducerConfigurationCatalog>())
                .IsTypeOf<FusionKohProducerConfigurationCatalog>();
            await Assert.That(scope.ServiceProvider.GetRequiredService<ILeaderboardCache>())
                .IsTypeOf<FusionLeaderboardCache>();

            var capacity = scope.ServiceProvider.GetRequiredService<IRunnerCapacityGate>();
            await Assert.That(await capacity.GetHeartbeatAsync(
                    "development",
                    "local-development",
                    cancellationToken))
                .IsEqualTo(RunnerHeartbeatStatus.Online);
            var providers = scope.ServiceProvider.GetRequiredService<IRuntimeProviderCatalog>();
            await Assert.That(providers.Containers(RuntimeProvider.Docker)).IsNotNull();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync(new ProjectLeaderboard(Guid.NewGuid()), cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync(new RuntimeProvisionFailed(
                    Guid.NewGuid(),
                    RuntimeFailureCode.RunnerUnavailable,
                    "local-development"), cancellationToken);
        }

        using var health = await client.GetAsync("/health", cancellationToken);
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { login = "integration-admin", password = "integration-password" },
            cancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var loginJson = JsonDocument.Parse(
            await login.Content.ReadAsStreamAsync(cancellationToken));
        var accessToken = loginJson.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var now = DateTimeOffset.UtcNow;
        using var created = await client.PostAsJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = "Development integration",
                description = "EF Core InMemory HTTP integration",
                mode = "Ctf",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(2),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 0
            },
            cancellationToken);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(
            await created.Content.ReadAsStreamAsync(cancellationToken));
        var competitionId = createdJson.RootElement.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync(new ProjectLeaderboard(competitionId), cancellationToken);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var leaderboard = await scope.ServiceProvider
                .GetRequiredService<ILeaderboardCache>()
                .GetAsync(competitionId, cancellationToken);
            await Assert.That(leaderboard).IsNotNull();
            await Assert.That(leaderboard!.CompetitionId).IsEqualTo(competitionId);
        }
    }
}

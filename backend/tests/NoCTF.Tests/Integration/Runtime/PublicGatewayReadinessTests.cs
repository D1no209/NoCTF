using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Hosting.Health;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.PublicAccess;
using NoCTF.Runtime.Docker.PublicAccess;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class PublicGatewayReadinessTests
{
    [Test, Timeout(180_000)]
    public async Task Readiness_tracks_real_ownership_initialization_loss_recovery_and_shutdown(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var redisContainer = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
            await Task.WhenAll(postgres.StartAsync(ct), redisContainer.StartAsync(ct));
            var redisOptions = ConfigurationOptions.Parse(redisContainer.GetConnectionString());
            redisOptions.AbortOnConnectFail = false;
            redisOptions.AsyncTimeout = 500;
            redisOptions.ConnectTimeout = 1000;
            using var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
            var connector = "readiness-" + Guid.NewGuid().ToString("N");
            var capability = new PublicGatewayCapability(connector, "runner", ["https://gateway.test"], 40000, 40255, [], 8, true);
            var leaseKey = "noctf:public-gateway:owner:" + connector;
            var database = redis.GetDatabase();
            await database.LockTakeAsync(leaseKey, "other-owner", TimeSpan.FromMinutes(2));

            // No Docker transport work is under test here; Redis ownership and PostgreSQL policy are real.
            var transport = Substitute.For<IPublicGatewayTransport>();
            var setupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.RevokeStaleHelpersAsync(Arg.Any<CancellationToken>())
                .Returns(call => setupGate.Task.WaitAsync(call.Arg<CancellationToken>()));
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
            builder.Services.AddSingleton(capability);
            builder.Services.AddSingleton(transport);
            builder.Services.AddSingleton(Substitute.For<IPublicGatewayStatusStore>());
            builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            builder.Services.AddSingleton<PublicGatewayAgent>();
            builder.Services.AddSingleton<IReadinessDependency, PublicGatewayReadinessDependency>();
            builder.Services.AddHealthChecks().AddCheck<RoleReadinessHealthCheck>("role-readiness", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));
            await using var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapNoCtfHealthChecks();
            await using (var scope = app.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().Database.EnsureCreatedAsync(ct);
            await app.StartAsync(ct);
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
            var agent = app.Services.GetRequiredService<PublicGatewayAgent>();
            async Task Expect(HttpStatusCode code)
            {
                using var response = await http.GetAsync("/health/ready", ct);
                await Assert.That(response.StatusCode).IsEqualTo(code);
                await Assert.That(await response.Content.ReadAsStringAsync(ct)).DoesNotContain("other-owner");
            }
            async Task EventuallyHealthy()
            {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
                while (DateTimeOffset.UtcNow < deadline)
                {
                    using var response = await http.GetAsync("/health/ready", ct);
                    if (response.StatusCode == HttpStatusCode.OK) return;
                    await Task.Delay(100, ct);
                }
                await Expect(HttpStatusCode.OK);
            }

            await Expect(HttpStatusCode.ServiceUnavailable); // Not started.
            await agent.StartAsync(ct);
            try
            {
                await Expect(HttpStatusCode.ServiceUnavailable); // Another owner, Redis itself is healthy.
                await database.LockReleaseAsync(leaseKey, "other-owner");
                var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
                while ((await database.LockQueryAsync(leaseKey)).IsNull && DateTimeOffset.UtcNow < deadline)
                    await Task.Delay(100, ct);
                await Assert.That((await database.LockQueryAsync(leaseKey)).IsNull).IsFalse();
                await Expect(HttpStatusCode.ServiceUnavailable); // Owns lock, but cleanup/setup incomplete.
                setupGate.SetResult();
                await EventuallyHealthy(); // Disabled policy still has a healthy initialized coordinator.

                await database.StringSetAsync(leaseKey, "other-owner", TimeSpan.FromMinutes(2));
                await Expect(HttpStatusCode.ServiceUnavailable); // Must not trust a stale local owner flag.
                await Task.Delay(2500, ct); // Exercise the failed renewal branch as well.
                await Expect(HttpStatusCode.ServiceUnavailable);
                await database.KeyDeleteAsync(leaseKey);
                await EventuallyHealthy();

                // Keep Redis reachable but reject ownership query/renewal commands. This isolates
                // the original false-positive readiness case without changing Docker's random port.
                await database.ExecuteAsync("ACL", "SETUSER", "default", "-get", "-eval", "-evalsha");
                try
                {
                    await Assert.That(redis.IsConnected).IsTrue();
                    await Expect(HttpStatusCode.ServiceUnavailable);
                    await Task.Delay(2500, ct);
                    await Expect(HttpStatusCode.ServiceUnavailable);
                }
                finally { await database.ExecuteAsync("ACL", "SETUSER", "default", "+get", "+eval", "+evalsha"); }
                await EventuallyHealthy();
            }
            finally
            {
                setupGate.TrySetResult();
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await agent.StopAsync(stop.Token);
            }
            await Expect(HttpStatusCode.ServiceUnavailable);
            using var live = await http.GetAsync("/health/live", ct);
            await Assert.That(live.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await app.StopAsync(ct);
        });
    }
}

using System.Net;
using System.IO.Compression;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Infrastructure.Competitions.Visibility;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Hosting.Messaging;
using NoCTF.Worker;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Teams.Moderation;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Wolverine;
using Wolverine.Nats;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000), NotInParallel]
    public Task Http_output_cache_reuses_public_bytes_and_checks_current_access_before_every_hit(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
        await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
            .WithPortBinding(4222, true).WithCommand("-js")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
        await Task.WhenAll(postgres.StartAsync(ct), redis.StartAsync(ct), nats.StartAsync(ct));
        var natsUrl = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
        using var messaging = Host.CreateDefaultBuilder().UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.UseNats(natsUrl).AutoProvision().UseJetStream(_ => { }).DefineStream("OUTPUT_TEST", stream =>
            {
                stream.WithSubject("output.projection");
                stream.Retention = NATS.Client.JetStream.Models.StreamConfigRetention.Workqueue;
            });
            options.PublishMessage<ProjectLeaderboard>().ToNatsSubject("output.projection").UseJetStream("OUTPUT_TEST");
        }).Build();
        await messaging.StartAsync(ct);
        await using var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Redis"] = redis.GetConnectionString() }).Build();
        await using var cacheServices = new ServiceCollection().AddNoCtfCaching(configuration, false).BuildServiceProvider();
        var caches = cacheServices.GetRequiredService<IFusionCacheProvider>();
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var owner = CreateUser(now);
        var fixture = CreateFixture(GameMode.Ctf, 0, owner.Id, now);
        fixture.CompetitionChallenge.Rules!.HasScoreCurve = true;
        fixture.CompetitionChallenge.Rules.ScoreCurve = new()
            { InitialPoints = 100, MinimumPoints = 100, DecayTeamCount = 10, DecayMode = PersistedScoreDecayMode.Fixed };
        fixture.Facts[0].Result = GameplayFactResult.Correct;
        db.Users.Add(owner); db.Competitions.Add(fixture.Competition); db.Challenges.Add(fixture.Challenge);
        db.CompetitionChallenges.Add(fixture.CompetitionChallenge); db.Teams.Add(fixture.Team); db.GameplayFacts.Add(fixture.Facts[0]);
        await db.SaveChangesAsync(ct);
        var fence = new NatsLeaderboardPublicationFence(connection, caches);
        var engine = new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog());
        var publisher = new NatsLeaderboardRefreshPublisher(connection);
        var bus = messaging.Services.GetRequiredService<IMessageBus>();
        FusionLeaderboardCache Cache(NoCtfDbContext context) => new(context, engine, publisher, caches, fence,
            workPublisher: new WolverinePostCommitMessagePublisher(bus, context));
        await Cache(db).RefreshAsync(fixture.Competition.Id, ct);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(auth =>
        {
            auth.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
                ValidateIssuer = true, ValidIssuer = "output-cache-test",
                ValidateAudience = true, ValidAudience = "output-cache-test", ValidateLifetime = true
            };
        });
        builder.Services.AddAuthorization(); builder.Services.AddProblemDetails();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<NoCtfDbContext>(configured => configured.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<ICompetitionVisibilityAccess, CompetitionVisibilityAccess>();
        builder.Services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
        builder.Services.AddScoped<IPostCommitMessagePublisher>(services => new WolverinePostCommitMessagePublisher(bus, services.GetRequiredService<NoCtfDbContext>()));
        builder.Services.AddScoped<ICompetitionEventRecorder, CompetitionEventStore>();
        builder.Services.AddScoped<ICompetitionTrackStore, CompetitionTrackStore>(); builder.Services.AddScoped<GetCompetitionTracks>();
        builder.Services.AddSingleton<IBackendMessagePublisher>(new WolverineBackendMessagePublisher(bus));
        builder.Services.AddScoped<ILeaderboardCache>(services => Cache(services.GetRequiredService<NoCtfDbContext>()));
        builder.Services.AddScoped<ILeaderboardSnapshotFactory>(services => (ILeaderboardSnapshotFactory)services.GetRequiredService<ILeaderboardCache>());
        builder.Services.AddNoCtfStaticAssetDelivery();
        builder.Services.AddNoCtfLeaderboardOutputCaching();
        builder.Services.AddFastEndpoints(discovery =>
        {
            discovery.DisableAutoDiscovery = true;
            discovery.Assemblies = [typeof(GetLeaderboardEndpoint).Assembly];
            discovery.Filter = type => type == typeof(GetLeaderboardEndpoint);
        });
        await using var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseOutputCache(); app.UseResponseCompression();
        var advancePublication = false;
        app.Use(async (context, next) =>
        {
            if (advancePublication && context.Request.Query.ContainsKey("advance"))
            {
                advancePublication = false;
                fixture.Facts[0].Result = GameplayFactResult.Wrong;
                await db.SaveChangesAsync(ct);
                await Cache(db).RefreshAsync(fixture.Competition.Id, ct);
            }
            await next(context);
        });
        app.UseNoCtfEndpoints();
        await app.StartAsync(ct);
        using var client = app.GetTestClient();
        var path = $"/api/v1/competitions/{fixture.Competition.Id}/leaderboard";
        using var first = await client.GetAsync(path, ct);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var original = await first.Content.ReadAsStringAsync(ct);
        using var second = await client.GetAsync(path, ct);
        await Assert.That(second.Headers.Contains("Age")).IsTrue();
        await Assert.That(await second.Content.ReadAsStringAsync(ct)).IsEqualTo(original);
        var token = new JwtSecurityToken("output-cache-test", "output-cache-test",
            [new Claim(ClaimTypes.NameIdentifier, owner.Id.ToString())], expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        var bearer = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        client.DefaultRequestHeaders.Authorization = bearer;
        using var authenticated = await client.GetAsync(path, ct);
        await Assert.That(authenticated.Headers.Contains("Age")).IsTrue();
        await Assert.That(await authenticated.Content.ReadAsStringAsync(ct)).IsEqualTo(original);
        client.DefaultRequestHeaders.Authorization = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + "?representation=gzip");
            request.Headers.AcceptEncoding.ParseAdd("gzip");
            using var compressed = await client.SendAsync(request, ct);
            await Assert.That(compressed.Content.Headers.ContentEncoding).Contains("gzip");
            if (attempt == 1) await Assert.That(compressed.Headers.Contains("Age")).IsTrue();
            await using var gzip = new GZipStream(await compressed.Content.ReadAsStreamAsync(ct), CompressionMode.Decompress);
            using var text = new StreamReader(gzip);
            await Assert.That(await text.ReadToEndAsync(ct)).IsEqualTo(original);
        }
        advancePublication = true;
        using var advancing = await client.GetAsync(path + "?advance=1", ct);
        using var advancingBody = JsonDocument.Parse(await advancing.Content.ReadAsStringAsync(ct));
        using var originalBody = JsonDocument.Parse(original);
        await Assert.That(advancingBody.RootElement.GetProperty("version").GetString())
            .IsEqualTo(originalBody.RootElement.GetProperty("version").GetString());
        fixture.Competition.AccessMode = CompetitionAccessMode.StaffOnly; await db.SaveChangesAsync(ct);
        await Cache(db).RefreshAsync(fixture.Competition.Id, ct);
        using var denied = await client.GetAsync(path, ct);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Authorization = bearer;
        using var staff = await client.GetAsync(path, ct);
        await Assert.That(staff.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(staff.Headers.Contains("Age")).IsFalse();
        client.DefaultRequestHeaders.Authorization = null;
        fixture.Competition.AccessMode = CompetitionAccessMode.Public; await db.SaveChangesAsync(ct);
        await Cache(db).RefreshAsync(fixture.Competition.Id, ct);

        fixture.Competition.HiddenStartAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(ct);
        using var hidden = await client.GetAsync(path, ct);
        using var hiddenBody = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync(ct));
        await Assert.That(hiddenBody.RootElement.GetProperty("dataScope").GetString()).IsEqualTo("Hidden");
        await Assert.That(hiddenBody.RootElement.GetProperty("teams").GetArrayLength()).IsEqualTo(0);
        fixture.Competition.HiddenStartAt = null; fixture.CompetitionChallenge.IsPublished = false; await db.SaveChangesAsync(ct);
        using var unavailable = await client.GetAsync(path, ct);
        await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Cache(db).RefreshAsync(fixture.Competition.Id, ct);
        using var updated = await client.GetAsync(path, ct);
        using var updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(ct));
        await Assert.That(updatedBody.RootElement.GetProperty("teams")[0].GetProperty("slots").GetArrayLength()).IsEqualTo(0);

        IHost BuildWorker() => Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(options);
            services.AddScoped<NoCtfDbContext>();
            services.AddSingleton<ILeaderboardProjectionEngine>(engine);
            services.AddSingleton<ILeaderboardRefreshPublisher>(publisher);
            services.AddSingleton(caches);
            services.AddSingleton<ILeaderboardPublicationFence>(fence);
            services.AddScoped<IPostCommitMessagePublisher, WolverinePostCommitMessagePublisher>();
            services.AddScoped<ILeaderboardCache, FusionLeaderboardCache>();
        }).UseWolverine(worker =>
        {
            worker.UseRuntimeCompilation();
            worker.Discovery.DisableConventionalDiscovery();
            worker.Discovery.IncludeType<LeaderboardMessageHandler>();
            worker.UseNats(natsUrl).AutoProvision().UseJetStream(_ => { }).DefineStream("OUTPUT_TEST", stream =>
            {
                stream.WithSubject("output.projection");
                stream.Retention = NATS.Client.JetStream.Models.StreamConfigRetention.Workqueue;
            });
            worker.ListenToNatsSubject("output.projection").UseJetStream("OUTPUT_TEST", "output-worker")
                .Named(WorkerQueueNames.Projection).MaximumParallelMessages(2);
            worker.ConfigureNoCtfInfrastructureRetriesFor<ProjectLeaderboard>(WorkerQueue.Projection, WorkerQueueNames.Projection);
        }).Build();

        async Task<string> WaitForScore(long expected)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                using var response = await client.GetAsync(path, ct);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (body.RootElement.GetProperty("teams")[0].GetProperty("totalScore").GetInt64() == expected)
                        return body.RootElement.GetProperty("version").GetString()!;
                }
                await Task.Delay(100, ct);
            }
            throw new TimeoutException("The real projection Worker did not publish the expected score.");
        }

        fixture.CompetitionChallenge.IsPublished = true;
        await db.SaveChangesAsync(ct);
        using (var worker = BuildWorker())
        {
            await worker.StartAsync(ct);
            try
            {
                await Cache(db).RequestRefreshAsync(fixture.Competition.Id, ct);
                _ = await WaitForScore(0);
            }
            finally { await worker.StopAsync(ct); }
        }
        var previousVersion = await WaitForScore(0);
        fixture.Facts[0].Result = GameplayFactResult.Correct;
        await db.SaveChangesAsync(ct);
        await Cache(db).RequestRefreshAsync(fixture.Competition.Id, ct);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(700, ct);
            await Assert.That(await WaitForScore(0)).IsEqualTo(previousVersion);
        }
        using (var restarted = BuildWorker())
        {
            await restarted.StartAsync(ct);
            try { await Assert.That(long.Parse(await WaitForScore(100))).IsGreaterThan(long.Parse(previousVersion)); }
            finally { await restarted.StopAsync(ct); }
        }
        await messaging.StopAsync(ct);
    });
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration")]
[NotInParallel]
public sealed class RedisPlatformLogStoreTests
{
    [Test]
    public async Task Production_registration_uses_fixed_retention_policy()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformLogs:MaximumEntriesPerDay"] = "1",
                ["PlatformLogs:RetentionDays"] = "1"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddNoCtfPlatformLogging(configuration, PlatformLogService.Api);
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PlatformLogWriterOptions>();

        await Assert.That(options.MaximumEntriesPerDay).IsEqualTo(50_000);
        await Assert.That(options.RetentionDays).IsEqualTo(14);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Three_process_log_contract_is_redacted_filterable_and_cursor_paged(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            using var provider = new RedisPlatformLoggerProvider(
                redis,
                new(PlatformLogService.Runner, 1_000, 14));
            var logger = provider.CreateLogger("NoCTF.Runner.Messages.RuntimeHandlers");
            var competitionId = Guid.NewGuid();
            var runtimeInstanceId = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var competitionChallengeId = Guid.NewGuid();
            var submissionId = Guid.NewGuid();
            const string password = "do-not-store-password";
            const string token = "do-not-store-token";
            const string flag = "flag{admin-can-see-this}";

            logger.LogInformation("Runner initialized.");
            logger.LogWarning(
                "Runtime {RuntimeInstanceId} for competition {CompetitionId}, team {TeamId}, user {UserId}, challenge {CompetitionChallengeId}, submission {SubmissionId} used password {Password}, token {Token}, and Flag {Flag}.",
                runtimeInstanceId,
                competitionId,
                teamId,
                userId,
                competitionChallengeId,
                submissionId,
                password,
                token,
                flag);
            var store = new RedisPlatformLogStore(
                redis,
                new(PlatformLogService.Runner, 1_000, 14));
            var warnings = await store.QueryAsync(
                new(
                    MinimumLevel: PlatformLogLevel.Warning,
                    Service: PlatformLogService.Runner,
                    From: null,
                    To: null,
                    Category: "NoCTF.Runner.Messages.RuntimeHandlers",
                    Search: "FLAG{ADMIN-CAN-SEE-THIS}",
                    CompetitionId: competitionId,
                    RuntimeInstanceId: runtimeInstanceId,
                    TeamId: teamId,
                    UserId: userId,
                    CompetitionChallengeId: competitionChallengeId,
                    SubmissionId: submissionId,
                    Cursor: null,
                    Limit: 1),
                cancellationToken);

            await Assert.That(warnings.State).IsEqualTo(PlatformLogReadState.Available);
            await Assert.That(warnings.Items).Count().IsEqualTo(1);
            await Assert.That(warnings.NextCursor).IsNotNull();
            await Assert.That(warnings.Items[0].Message).DoesNotContain(password);
            await Assert.That(warnings.Items[0].Message).DoesNotContain(token);
            await Assert.That(warnings.Items[0].Message).Contains(flag);
            await Assert.That(warnings.Items[0].CompetitionId).IsEqualTo(competitionId);
            await Assert.That(warnings.Items[0].RuntimeInstanceId).IsEqualTo(runtimeInstanceId);
            await Assert.That(warnings.Items[0].TeamId).IsEqualTo(teamId);
            await Assert.That(warnings.Items[0].UserId).IsEqualTo(userId);
            await Assert.That(warnings.Items[0].CompetitionChallengeId)
                .IsEqualTo(competitionChallengeId);
            await Assert.That(warnings.Items[0].SubmissionId).IsEqualTo(submissionId);
            await Assert.That(warnings.Items[0].Cursor).StartsWith(
                $"{DateTimeOffset.UtcNow:yyyyMMdd}:");

            var streamKey = RedisPlatformLoggerProvider.StreamKeyFor(
                DateOnly.FromDateTime(DateTime.UtcNow));
            var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(streamKey);
            await Assert.That(ttl).IsNotNull();
            await Assert.That(ttl!.Value).IsGreaterThan(TimeSpan.FromDays(12));
            await Assert.That(ttl.Value).IsLessThanOrEqualTo(TimeSpan.FromDays(14));

            var nextPage = await store.QueryAsync(
                new(
                    MinimumLevel: PlatformLogLevel.Information,
                    Service: PlatformLogService.Runner,
                    From: null,
                    To: null,
                    Category: null,
                    Search: null,
                    CompetitionId: null,
                    RuntimeInstanceId: null,
                    TeamId: null,
                    UserId: null,
                    CompetitionChallengeId: null,
                    SubmissionId: null,
                    Cursor: warnings.NextCursor,
                    Limit: 10),
                cancellationToken);
            await Assert.That(nextPage.Items).Count().IsEqualTo(1);
            await Assert.That(nextPage.Items[0].Level).IsEqualTo(PlatformLogLevel.Information);

            var exported = await store.ExportAsync(
                new(
                    MinimumLevel: PlatformLogLevel.Warning,
                    Service: PlatformLogService.Runner,
                    From: DateTimeOffset.UtcNow.AddHours(-1),
                    To: DateTimeOffset.UtcNow.AddHours(1),
                    Category: null,
                    Search: "admin-can-see-this",
                    CompetitionId: competitionId,
                    RuntimeInstanceId: runtimeInstanceId,
                    TeamId: teamId,
                    UserId: userId,
                    CompetitionChallengeId: competitionChallengeId,
                    SubmissionId: submissionId,
                    Cursor: null,
                    Limit: 50_000),
                cancellationToken);
            await Assert.That(exported.State).IsEqualTo(PlatformLogReadState.Available);
            await using var exportStream = exported.Export!.Content;
            using var reader = new StreamReader(exportStream);
            var jsonl = await reader.ReadToEndAsync(cancellationToken);
            await Assert.That(jsonl).Contains(flag);
            await Assert.That(jsonl).DoesNotContain(password);
            await Assert.That(jsonl).DoesNotContain(token);
        });
    }

    [Test]
    [Timeout(120_000)]
    public async Task Daily_partition_uses_exact_entry_cap(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new RedisBuilder("redis:7-alpine").Build();
            await container.StartAsync(cancellationToken);
            await using var redis = await ConnectionMultiplexer.ConnectAsync(
                container.GetConnectionString());
            using var provider = new RedisPlatformLoggerProvider(
                redis,
                new(PlatformLogService.Api, 3, 14));
            var logger = provider.CreateLogger("NoCTF.API.CapProbe");
            for (var index = 0; index < 4; index++)
                logger.LogWarning("Daily cap probe {Index}.", index);

            var streamKey = RedisPlatformLoggerProvider.StreamKeyFor(
                DateOnly.FromDateTime(DateTime.UtcNow));
            await Assert.That(await redis.GetDatabase().StreamLengthAsync(streamKey))
                .IsEqualTo(3);
        });
    }
}

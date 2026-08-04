using Microsoft.Extensions.Logging;
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
                new(PlatformLogService.Runner, 1_000));
            var logger = provider.CreateLogger("NoCTF.Runner.Messages.RuntimeHandlers");
            var competitionId = Guid.NewGuid();
            var runtimeInstanceId = Guid.NewGuid();
            const string password = "do-not-store-password";
            const string token = "do-not-store-token";
            const string flag = "flag{admin-can-see-this}";

            logger.LogInformation("Runner initialized.");
            logger.LogWarning(
                "Runtime {RuntimeInstanceId} for competition {CompetitionId} used password {Password}, token {Token}, and Flag {Flag}.",
                runtimeInstanceId,
                competitionId,
                password,
                token,
                flag);
            var store = new RedisPlatformLogStore(redis);
            var warnings = await store.QueryAsync(
                new(
                    PlatformLogLevel.Warning,
                    PlatformLogService.Runner,
                    null,
                    null,
                    competitionId,
                    runtimeInstanceId,
                    null,
                    1),
                cancellationToken);

            await Assert.That(warnings.State).IsEqualTo(PlatformLogReadState.Available);
            await Assert.That(warnings.Items).Count().IsEqualTo(1);
            await Assert.That(warnings.NextCursor).IsNotNull();
            await Assert.That(warnings.Items[0].Message).DoesNotContain(password);
            await Assert.That(warnings.Items[0].Message).DoesNotContain(token);
            await Assert.That(warnings.Items[0].Message).Contains(flag);
            await Assert.That(warnings.Items[0].CompetitionId).IsEqualTo(competitionId);
            await Assert.That(warnings.Items[0].RuntimeInstanceId).IsEqualTo(runtimeInstanceId);

            var nextPage = await store.QueryAsync(
                new(
                    PlatformLogLevel.Information,
                    PlatformLogService.Runner,
                    null,
                    null,
                    null,
                    null,
                    warnings.NextCursor,
                    10),
                cancellationToken);
            await Assert.That(nextPage.Items).Count().IsEqualTo(1);
            await Assert.That(nextPage.Items[0].Level).IsEqualTo(PlatformLogLevel.Information);
        });
    }
}

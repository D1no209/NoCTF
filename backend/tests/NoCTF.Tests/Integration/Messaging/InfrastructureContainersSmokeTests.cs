using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
public sealed class InfrastructureContainersSmokeTests
{
    [Test]
    [Timeout(300_000)]
    public async Task PostgreSql_and_Redis_containers_are_reachable(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();

            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                redis.StartAsync(cancellationToken));

            await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select 1", connection);
            await Assert.That(await command.ExecuteScalarAsync(cancellationToken)).IsEqualTo(1);

            await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
            await Assert.That(await multiplexer.GetDatabase().PingAsync()).IsLessThan(TimeSpan.FromSeconds(1));
        });
    }
}

using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Fixtures.PostgreSql;

public sealed class PostgreSqlContainerFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
        .WithDatabase("noctf_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();
    public ValueTask DisposeAsync() => container.DisposeAsync();
}

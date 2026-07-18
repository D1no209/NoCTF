using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Fixtures.PostgreSql;

public sealed class PostgreSqlContainerFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("noctf_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();
    public ValueTask DisposeAsync() => container.DisposeAsync();
}

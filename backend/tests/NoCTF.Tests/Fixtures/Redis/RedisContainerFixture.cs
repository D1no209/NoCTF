using Testcontainers.Redis;

namespace NoCTF.Tests.Fixtures.Redis;

public sealed class RedisContainerFixture : IAsyncDisposable
{
    private readonly RedisContainer container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();
    public ValueTask DisposeAsync() => container.DisposeAsync();
}

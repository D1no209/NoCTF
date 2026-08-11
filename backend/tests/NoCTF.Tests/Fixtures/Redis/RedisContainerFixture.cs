using Testcontainers.Redis;

namespace NoCTF.Tests.Fixtures.Redis;

public sealed class RedisContainerFixture : IAsyncDisposable
{
    private readonly RedisContainer container = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();
    public ValueTask DisposeAsync() => container.DisposeAsync();
}

using System.Text.Json;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Theory.Realtime;

namespace NoCTF.Infrastructure.Theory.Realtime;

public sealed class NatsTheoryChangePublisher(INatsConnection connection) : ITheoryChangePublisher
{
    public const string Subject = "noctf.v3.ui.theory";
    public async Task PublishAsync(TheoryChanged change, CancellationToken ct) => await connection.PublishAsync(Subject,
        JsonSerializer.SerializeToUtf8Bytes(change, NoCtfMessageJsonContext.Default.TheoryChanged), cancellationToken: ct);
}
public sealed class NoOpTheoryChangePublisher : ITheoryChangePublisher
{
    public Task PublishAsync(TheoryChanged change, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}

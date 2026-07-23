using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Messaging;

public sealed class WolverineTransactionalMessageOutbox(
    IDbContextOutbox<NoCtfDbContext> outbox) : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => outbox.PublishAsync(message);
    public Task FlushOutgoingMessagesAsync() => outbox.FlushOutgoingMessagesAsync();
}

public sealed class OpenApiTransactionalMessageOutbox : ITransactionalMessageOutbox
{
    public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
    public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
}

using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class WolverineTransactionalMessageOutboxTests
{
    [Test]
    public async Task Scoped_outbox_allows_messages_after_an_earlier_save_flush()
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase($"outbox-multi-flush-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NoCtfDbContext(options);
        var context = new TestDbContextOutbox(
            Substitute.For<IWolverineRuntime>(),
            db);

        _ = new WolverineTransactionalMessageOutbox(context);

        await Assert.That(context.MultiFlushMode)
            .IsEqualTo(MultiFlushMode.AllowMultiples);
    }

    [Test]
    public async Task Account_message_text_never_contains_a_raw_token()
    {
        const string token = "secret-email-verification-token";
        var message = new SendEmailVerification(Guid.NewGuid(), token);

        await Assert.That(message.ToString()).Contains("[REDACTED]");
        await Assert.That(message.ToString()).DoesNotContain(token);
    }

    private sealed class TestDbContextOutbox(
        IWolverineRuntime runtime,
        NoCtfDbContext db)
        : MessageContext(runtime), IDbContextOutbox<NoCtfDbContext>
    {
        public NoCtfDbContext DbContext { get; } = db;

        public Task SaveChangesAndFlushMessagesAsync(CancellationToken token) =>
            Task.CompletedTask;

        public Task SaveChangesAndFlushMessagesAsync(
            MultiFlushMode flushMode,
            CancellationToken token) => Task.CompletedTask;

        Task IDbContextOutbox<NoCtfDbContext>.FlushOutgoingMessagesAsync() =>
            Task.CompletedTask;
    }
}

using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Wolverine;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class WolverinePostCommitMessagePublisherTests
{
    [Test]
    public async Task Publisher_buffers_messages_until_the_database_transaction_has_completed()
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseInMemoryDatabase($"outbox-multi-flush-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NoCtfDbContext(options);
        var bus = Substitute.For<IMessageBus>();
        var publisher = new WolverinePostCommitMessagePublisher(bus, db);
        var message = new SendEmailVerification(Guid.NewGuid(), "redacted-token");

        await publisher.PublishAsync(message);
        await Assert.That(bus.ReceivedCalls()).IsEmpty();

        await publisher.FlushCommittedMessagesAsync();
        await Assert.That(bus.ReceivedCalls().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Committed_flush_publishes_before_EF_transaction_is_disposed()
    {
        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var db = new NoCtfDbContext(options);
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var bus = Substitute.For<IMessageBus>();
        var publisher = new WolverinePostCommitMessagePublisher(bus, db);

        await publisher.PublishAsync(new SendEmailVerification(Guid.NewGuid(), "redacted-token"));
        await publisher.FlushOutgoingMessagesAsync();
        await Assert.That(bus.ReceivedCalls()).IsEmpty();

        await transaction.CommitAsync();
        await publisher.FlushCommittedMessagesAsync();
        await Assert.That(bus.ReceivedCalls().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Account_message_text_never_contains_a_raw_token()
    {
        const string token = "secret-email-verification-token";
        var message = new SendEmailVerification(Guid.NewGuid(), token);

        await Assert.That(message.ToString()).Contains("[REDACTED]");
        await Assert.That(message.ToString()).DoesNotContain(token);
    }
}

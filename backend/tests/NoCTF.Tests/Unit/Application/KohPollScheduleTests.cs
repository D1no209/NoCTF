using NoCTF.Application.Competitions.Koh;
using NoCTF.Worker;
using Wolverine.Attributes;

namespace NoCTF.Tests.Unit.Application;

public sealed class KohPollScheduleTests
{
    [Test]
    public async Task Next_due_skips_missed_intervals_without_backfill()
    {
        var due = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

        var next = KohPollSchedule.NextDue(
            due,
            due.AddSeconds(16),
            TimeSpan.FromSeconds(5));

        await Assert.That(next).IsEqualTo(due.AddSeconds(20));
    }

    [Test]
    public async Task Future_due_is_preserved()
    {
        var due = new DateTimeOffset(2026, 7, 24, 12, 0, 5, TimeSpan.Zero);

        var next = KohPollSchedule.NextDue(
            due,
            due.AddSeconds(-1),
            TimeSpan.FromSeconds(5));

        await Assert.That(next).IsEqualTo(due);
    }

    [Test]
    public async Task External_poll_is_non_transactional_and_fact_writer_uses_explicit_transaction()
    {
        var pollAttributes = typeof(KohPollingHandler)
            .GetCustomAttributes(typeof(NonTransactionalAttribute), inherit: true);
        var writeAttributes = typeof(KohObservationHandler)
            .GetCustomAttributes(typeof(TransactionalAttribute), inherit: true);

        await Assert.That(pollAttributes).HasSingleItem();
        await Assert.That(writeAttributes).IsEmpty();
    }
}

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Persistence;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotStateStoreTests
{
    [Test]
    public async Task Store_PersistsSubscriptionDedupeAndRetryState()
    {
        var directory = Directory.CreateTempSubdirectory("noctf-bot-test-");
        try
        {
            var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-20T00:00:00Z"));
            using var store = new BotStateStore(
                Options.Create(new RelayOptions
                {
                    StatePath = Path.Combine(directory.FullName, "state.sqlite3")
                }),
                time);
            store.Initialize();
            var competitionId = Guid.NewGuid();

            store.UpsertSubscription(10001, competitionId);
            var subscription = store.GetSubscription(10001);
            var firstInbound = store.TryRecordInbound("group", 10001, 7);
            var duplicateInbound = store.TryRecordInbound("group", 10001, 7);
            var firstOutbound = store.EnqueueOutbound("event-1", 10001, "hello");
            var duplicateOutbound = store.EnqueueOutbound("event-1", 10001, "hello");
            var claimed = store.ClaimDueOutbound();
            store.FailOutbound(claimed!.Id, claimed.AttemptCount, "temporary");

            await Assert.That(subscription).IsNotNull();
            await Assert.That(subscription!.CompetitionId).IsEqualTo(competitionId);
            await Assert.That(firstInbound).IsTrue();
            await Assert.That(duplicateInbound).IsFalse();
            await Assert.That(firstOutbound).IsTrue();
            await Assert.That(duplicateOutbound).IsFalse();
            await Assert.That(claimed.AttemptCount).IsEqualTo(1);
            await Assert.That(store.ClaimDueOutbound()).IsNull();

            time.Advance(TimeSpan.FromSeconds(2));

            await Assert.That(store.ClaimDueOutbound()).IsNotNull();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RetrySchedule_StopsAfterConfiguredBackoffSeries()
    {
        await Assert.That(OutboundRetrySchedule.AfterFailure(1)).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(OutboundRetrySchedule.AfterFailure(6)).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(OutboundRetrySchedule.AfterFailure(7)).IsNull();
    }
}

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.Sqlite;
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

            store.AuthorizeAndEnableGroup("fake", "10001");
            store.UpsertSubscription("fake", "10001", competitionId);
            var subscription = store.GetSubscription("fake", "10001");
            var firstInbound = store.TryRecordInbound("fake", "10001", "7");
            var duplicateInbound = store.TryRecordInbound("fake", "10001", "7");
            var firstOutbound = store.EnqueueOutbound("event-1", "fake", "10001", "hello");
            var duplicateOutbound = store.EnqueueOutbound("event-1", "fake", "10001", "hello");
            var claimed = store.ClaimDueOutbound("fake");
            store.FailOutbound(claimed!.Id, claimed.AttemptCount, "temporary");

            await Assert.That(subscription).IsNotNull();
            await Assert.That(subscription!.CompetitionId).IsEqualTo(competitionId);
            await Assert.That(firstInbound).IsTrue();
            await Assert.That(duplicateInbound).IsFalse();
            await Assert.That(firstOutbound).IsTrue();
            await Assert.That(duplicateOutbound).IsFalse();
            await Assert.That(claimed.AttemptCount).IsEqualTo(1);
            await Assert.That(store.ClaimDueOutbound("fake")).IsNull();

            time.Advance(TimeSpan.FromSeconds(2));

            await Assert.That(store.ClaimDueOutbound("fake")).IsNotNull();
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

    [Test]
    public async Task GroupAccess_DisableAndRevoke_EnforceMasterAuthorizationBoundary()
    {
        var directory = Directory.CreateTempSubdirectory("noctf-bot-access-");
        try
        {
            using var store = CreateStore(directory.FullName);
            store.Initialize();
            var competitionId = Guid.NewGuid();

            store.AuthorizeAndEnableGroup("fake", "10001");
            store.AddGroupAdmin("fake", "10001", "20002", "master");
            store.UpsertSubscription("fake", "10001", competitionId);
            store.SetGroupEnabled("fake", "10001", false);

            await Assert.That(store.GetGroupAccess("fake", "10001")!.Enabled).IsFalse();
            await Assert.That(store.IsGroupAdmin("fake", "10001", "20002")).IsTrue();
            await Assert.That(store.GetSubscription("fake", "10001")).IsNull();

            store.SetGroupEnabled("fake", "10001", true);
            await Assert.That(store.GetSubscription("fake", "10001")).IsNotNull();

            store.RevokeGroup("fake", "10001");
            await Assert.That(store.GetGroupAccess("fake", "10001")).IsNull();
            await Assert.That(store.IsGroupAdmin("fake", "10001", "20002")).IsFalse();
            await Assert.That(store.GetSubscription("fake", "10001")).IsNull();
            store.Dispose();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Initialize_LegacyMilkySchema_PreservesBindingButRequiresMasterEnable()
    {
        var directory = Directory.CreateTempSubdirectory("noctf-bot-migration-");
        var statePath = Path.Combine(directory.FullName, "state.sqlite3");
        try
        {
            var competitionId = Guid.NewGuid();
            await using (var connection = new SqliteConnection($"Data Source={statePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE group_subscriptions (
                        group_id INTEGER PRIMARY KEY, competition_id TEXT NOT NULL,
                        broadcast_enabled INTEGER NOT NULL, scoreboard_enabled INTEGER NOT NULL,
                        blood_enabled INTEGER NOT NULL, suspended_reason TEXT NULL, updated_at TEXT NOT NULL);
                    CREATE INDEX ix_group_subscriptions_competition ON group_subscriptions(competition_id);
                    CREATE TABLE inbound_messages (
                        scene TEXT NOT NULL, peer_id INTEGER NOT NULL, message_sequence INTEGER NOT NULL,
                        received_at TEXT NOT NULL, PRIMARY KEY(scene, peer_id, message_sequence));
                    CREATE TABLE outbound_messages (
                        id TEXT PRIMARY KEY, dedupe_key TEXT NOT NULL UNIQUE, group_id INTEGER NOT NULL,
                        payload TEXT NOT NULL, state INTEGER NOT NULL, attempt_count INTEGER NOT NULL,
                        next_attempt_at TEXT NOT NULL, last_error TEXT NULL,
                        created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
                    CREATE INDEX ix_outbound_messages_due ON outbound_messages(state, next_attempt_at, created_at);
                    CREATE TABLE competition_snapshots (
                        competition_id TEXT PRIMARY KEY, status TEXT NOT NULL, start_time TEXT NOT NULL,
                        end_time TEXT NOT NULL, content_hash TEXT NOT NULL, updated_at TEXT NOT NULL);
                    CREATE TABLE challenge_snapshots (
                        competition_id TEXT NOT NULL, challenge_id TEXT NOT NULL, title TEXT NOT NULL,
                        published INTEGER NOT NULL, content_hash TEXT NOT NULL,
                        PRIMARY KEY(competition_id, challenge_id));
                    CREATE TABLE team_snapshots (
                        competition_id TEXT NOT NULL, team_id TEXT NOT NULL, team_name TEXT NOT NULL,
                        rank INTEGER NULL, score INTEGER NOT NULL, achievement_hash TEXT NOT NULL,
                        achievements_json TEXT NOT NULL, PRIMARY KEY(competition_id, team_id));
                    INSERT INTO group_subscriptions VALUES(
                        10001, $competitionId, 1, 0, 1, NULL, '2026-09-20T00:00:00.0000000+00:00');
                    """;
                command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                await command.ExecuteNonQueryAsync();
            }

            using var store = CreateStore(directory.FullName);
            store.Initialize();

            var access = store.GetGroupAccess("milky", "10001");
            await Assert.That(access).IsNotNull();
            await Assert.That(access!.MasterAuthorizedAt).IsNull();
            await Assert.That(access.Enabled).IsFalse();
            await Assert.That(store.GetSubscription("milky", "10001")).IsNull();

            store.AuthorizeAndEnableGroup("milky", "10001");
            var subscription = store.GetSubscription("milky", "10001");
            await Assert.That(subscription).IsNotNull();
            await Assert.That(subscription!.CompetitionId).IsEqualTo(competitionId);
            await Assert.That(subscription.ScoreboardEnabled).IsFalse();
            store.Dispose();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static BotStateStore CreateStore(string directory) =>
        new(
            Options.Create(new RelayOptions
            {
                StatePath = Path.Combine(directory, "state.sqlite3"),
                Provider = "fake",
                MasterUserId = "master"
            }),
            new FakeTimeProvider(DateTimeOffset.Parse("2026-09-20T00:00:00Z")));
}

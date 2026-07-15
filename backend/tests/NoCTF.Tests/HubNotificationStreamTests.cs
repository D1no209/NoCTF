using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API.SignalR;
using NoCTF.Application;
using NoCTF.Application.Notifications;
using StackExchange.Redis;

namespace NoCTF.Tests;

public class HubNotificationStreamTests
{
    [Fact]
    public async Task Envelope_RoundTripBuildsTypedRelayDispatch()
    {
        var competitionId = Guid.NewGuid();
        var envelope = HubNotificationEnvelope.Create(
            HubNotificationTypes.RoundStarted,
            competitionId,
            new RoundStartedNotification(7));
        var streamEntry = new StreamEntry("1-0", envelope.ToStreamValues());

        Assert.True(HubNotificationEnvelope.TryParse(streamEntry, out var parsed, out var parseError), parseError);
        Assert.True(
            RedisHubNotificationRelay.TryCreateDispatch(parsed!, out var dispatch, out var dispatchError),
            dispatchError);
        var target = new RecordingRelayTarget();

        await dispatch!(target, CancellationToken.None);

        Assert.Equal((competitionId, 7), Assert.Single(target.RoundStarted));
    }

    [Fact]
    public void RelayRejectsUnknownSchemaBeforeForwarding()
    {
        var envelope = HubNotificationEnvelope.Create(
            HubNotificationTypes.RoundStarted,
            Guid.NewGuid(),
            new RoundStartedNotification(1)) with
        {
            SchemaVersion = HubNotificationEnvelope.CurrentSchemaVersion + 1
        };

        Assert.False(RedisHubNotificationRelay.TryCreateDispatch(envelope, out var dispatch, out var error));
        Assert.Null(dispatch);
        Assert.Contains("schema", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyScoreUpdate_RelaysTheLatestCompleteLeaderboard()
    {
        var competitionId = Guid.NewGuid();
        var envelope = HubNotificationEnvelope.Create(
            HubNotificationTypes.ScoreUpdate,
            competitionId,
            new ScoreUpdateNotification(Guid.NewGuid(), "team", 42, 1));

        Assert.True(
            RedisHubNotificationRelay.TryCreateDispatch(envelope, out var dispatch, out var error),
            error);
        var target = new RecordingRelayTarget();

        await dispatch!(target, CancellationToken.None);

        Assert.Equal(competitionId, Assert.Single(target.LatestLeaderboardSnapshots));
    }

    [Fact]
    public async Task WorkerScoreUpdate_PublishesOnlyACompleteLeaderboardRefresh()
    {
        var competitionId = Guid.NewGuid();
        NameValueEntry[]? published = null;
        var configuration = BuildConfiguration("notifications", "notifications:dlq", "relays");
        var publisher = new RedisStreamHubNotifier(
            configuration,
            (_, values, _) =>
            {
                published = values;
                return Task.CompletedTask;
            });

        await publisher.NotifyScoreUpdateAsync(
            competitionId,
            Guid.NewGuid(),
            "team",
            42,
            1);

        Assert.NotNull(published);
        var entry = new StreamEntry("1-0", published!);
        Assert.True(HubNotificationEnvelope.TryParse(entry, out var envelope, out var error), error);
        Assert.Equal(HubNotificationTypes.LeaderboardRefresh, envelope!.Type);
        Assert.Equal(competitionId, envelope.CompetitionId);
    }

    [Fact]
    public async Task OversizedNotification_IsRejectedBeforeItCanBeAppended()
    {
        var appendCalls = 0;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:RedisStream:MaxPayloadBytes"] = "1024"
            })
            .Build();
        var publisher = new RedisStreamHubNotifier(
            configuration,
            (_, _, _) =>
            {
                appendCalls++;
                return Task.CompletedTask;
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.NotifySystemAlertAsync(
                Guid.NewGuid(),
                "error",
                new string('x', 2048)));

        Assert.Contains("1024", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, appendCalls);
    }

    [Fact]
    public void StatefulOrdering_IsScopedToTheUpdatedChallenge()
    {
        var competitionId = Guid.NewGuid();
        var firstChallengeId = Guid.NewGuid();
        var secondChallengeId = Guid.NewGuid();
        var first = HubNotificationEnvelope.Create(
            HubNotificationTypes.ChallengeUpdate,
            competitionId,
            new ChallengeUpdateNotification(firstChallengeId, "first", "updated"));
        var firstAgain = HubNotificationEnvelope.Create(
            HubNotificationTypes.ChallengeUpdate,
            competitionId,
            new ChallengeUpdateNotification(firstChallengeId, "first", "deleted"));
        var second = HubNotificationEnvelope.Create(
            HubNotificationTypes.ChallengeUpdate,
            competitionId,
            new ChallengeUpdateNotification(secondChallengeId, "second", "updated"));

        Assert.Equal(
            RedisHubNotificationRelay.StatefulOrderingKey(first),
            RedisHubNotificationRelay.StatefulOrderingKey(firstAgain));
        Assert.NotEqual(
            RedisHubNotificationRelay.StatefulOrderingKey(first),
            RedisHubNotificationRelay.StatefulOrderingKey(second));
    }

    [Fact]
    public async Task RedisStream_CompetingRelaysForwardOnceAndDeadLetterPoisonMessages()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var suffix = Guid.NewGuid().ToString("N");
        var streamKey = $"noctf:test:notifications:{suffix}";
        var deadLetterKey = $"{streamKey}:dlq";
        var group = $"test-relays-{suffix}";
        var configuration = BuildConfiguration(streamKey, deadLetterKey, group);
        var publisher = new RedisStreamHubNotifier(redis, configuration);
        var firstTarget = new RecordingRelayTarget();
        var secondTarget = new RecordingRelayTarget();
        var firstRelay = new RedisHubNotificationRelay(
            redis,
            firstTarget,
            configuration,
            NullLogger<RedisHubNotificationRelay>.Instance);
        var secondRelay = new RedisHubNotificationRelay(
            redis,
            secondTarget,
            configuration,
            NullLogger<RedisHubNotificationRelay>.Instance);
        var competitionId = Guid.NewGuid();

        try
        {
            await publisher.NotifyRoundStartedAsync(competitionId, 4);
            await firstRelay.EnsureConsumerGroupAsync();
            Assert.Equal(0, await firstRelay.RecoverPendingAsync());

            Assert.Equal(1, await firstRelay.ProcessNewMessagesAsync(block: null));
            Assert.Equal(0, await secondRelay.ProcessNewMessagesAsync(block: null));
            Assert.Equal((competitionId, 4), Assert.Single(firstTarget.RoundStarted));
            Assert.Empty(secondTarget.RoundStarted);

            var database = redis.GetDatabase();
            var duplicateEnvelope = HubNotificationEnvelope.Create(
                HubNotificationTypes.RoundStarted,
                competitionId,
                new RoundStartedNotification(5));
            await database.StreamAddAsync(streamKey, duplicateEnvelope.ToStreamValues());
            await database.StreamAddAsync(streamKey, duplicateEnvelope.ToStreamValues());

            Assert.Equal(2, await firstRelay.ProcessNewMessagesAsync(block: null));
            Assert.Equal(2, firstTarget.RoundStarted.Count);
            Assert.Single(firstTarget.RoundStarted, item => item.RoundNumber == 5);

            await database.StreamAddAsync(streamKey,
            [
                new NameValueEntry("eventId", "not-a-guid"),
                new NameValueEntry("payload", "{}")
            ]);

            Assert.Equal(1, await firstRelay.ProcessNewMessagesAsync(block: null));
            Assert.Equal(1, await database.StreamLengthAsync(deadLetterKey));
            Assert.Equal(0, (await database.StreamPendingAsync(streamKey, group)).PendingMessageCount);
        }
        finally
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync([streamKey, deadLetterKey]);
        }
    }

    [Fact]
    public async Task RedisStream_ForwardFailureRemainsPending()
    {
        var connectionString = Environment.GetEnvironmentVariable("NOCTF_REDIS_INTEGRATION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var suffix = Guid.NewGuid().ToString("N");
        var streamKey = $"noctf:test:notifications:{suffix}";
        var deadLetterKey = $"{streamKey}:dlq";
        var group = $"test-relays-{suffix}";
        var configuration = BuildConfiguration(streamKey, deadLetterKey, group);
        var publisher = new RedisStreamHubNotifier(redis, configuration);
        var target = new RecordingRelayTarget { FailRoundStarted = true };
        var relay = new RedisHubNotificationRelay(
            redis,
            target,
            configuration,
            NullLogger<RedisHubNotificationRelay>.Instance);

        try
        {
            await publisher.NotifyRoundStartedAsync(Guid.NewGuid(), 2);
            await relay.EnsureConsumerGroupAsync();

            Assert.Equal(1, await relay.ProcessNewMessagesAsync(block: null));
            Assert.Equal(
                1,
                (await redis.GetDatabase().StreamPendingAsync(streamKey, group)).PendingMessageCount);
        }
        finally
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync([streamKey, deadLetterKey]);
        }
    }

    private static IConfiguration BuildConfiguration(string streamKey, string deadLetterKey, string group)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:RedisStream:Key"] = streamKey,
                ["Notifications:RedisStream:DeadLetterKey"] = deadLetterKey,
                ["Notifications:RedisStream:ConsumerGroup"] = group,
                ["Notifications:RedisStream:ClaimIdleSeconds"] = "1",
                ["Notifications:RedisStream:BatchSize"] = "10"
            })
            .Build();

    private sealed class RecordingRelayTarget : IHubNotificationRelayTarget
    {
        public List<(Guid CompetitionId, int RoundNumber)> RoundStarted { get; } = [];
        public List<Guid> LatestLeaderboardSnapshots { get; } = [];
        public bool FailRoundStarted { get; init; }

        public Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
        {
            if (FailRoundStarted)
                throw new InvalidOperationException("relay unavailable");
            RoundStarted.Add((competitionId, roundNumber));
            return Task.CompletedTask;
        }

        public Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLatestLeaderboardSnapshotAsync(Guid competitionId, CancellationToken ct = default)
        {
            LatestLeaderboardSnapshots.Add(competitionId);
            return Task.CompletedTask;
        }
        public Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyAttackLogAsync(Guid competitionId, Guid attackerTeamId, string attackerTeamName, Guid victimTeamId, string victimTeamName, Guid challengeId, string challengeName, int roundNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default) => Task.CompletedTask;
    }
}

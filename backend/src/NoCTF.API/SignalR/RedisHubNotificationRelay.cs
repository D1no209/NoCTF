using System.Text.Json;
using NoCTF.Application;
using NoCTF.Application.Notifications;
using StackExchange.Redis;

namespace NoCTF.API.SignalR;

public interface IHubNotificationRelayTarget : IHubNotifierService
{
    Task NotifyLatestLeaderboardSnapshotAsync(Guid competitionId, CancellationToken ct = default);
}

/// <summary>
/// Relays durable Worker notifications to SignalR. A Redis consumer group
/// makes API replicas compete for each event; entries are acknowledged only
/// after a successful SignalR forward.
/// </summary>
public sealed class RedisHubNotificationRelay : BackgroundService
{
    private const string ReleaseProcessingLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private const string RenewProcessingLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string AdvanceWatermarkScript = """
        local current = redis.call('GET', KEYS[1])
        local incoming = ARGV[1]
        if current then
            local currentMs, currentSeq = string.match(current, '^(%d+)%-(%d+)$')
            local incomingMs, incomingSeq = string.match(incoming, '^(%d+)%-(%d+)$')
            if currentMs and incomingMs then
                currentMs = tonumber(currentMs)
                currentSeq = tonumber(currentSeq)
                incomingMs = tonumber(incomingMs)
                incomingSeq = tonumber(incomingSeq)
                if incomingMs < currentMs or (incomingMs == currentMs and incomingSeq <= currentSeq) then
                    return 0
                end
            elseif incoming <= current then
                return 0
            end
        end
        redis.call('SET', KEYS[1], incoming, 'PX', ARGV[2])
        return 1
        """;

    private readonly IDatabase _database;
    private readonly IHubNotificationRelayTarget _target;
    private readonly ILogger<RedisHubNotificationRelay> _logger;
    private readonly HubNotificationStreamSettings _settings;
    private readonly RedisValue _consumerName;

    public RedisHubNotificationRelay(
        IConnectionMultiplexer redis,
        IHubNotificationRelayTarget target,
        IConfiguration configuration,
        ILogger<RedisHubNotificationRelay> logger)
    {
        _database = redis.GetDatabase();
        _target = target;
        _logger = logger;
        _settings = HubNotificationStreamSettings.FromConfiguration(configuration);
        _consumerName = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var groupReady = false;
        var nextRecoveryAt = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!groupReady)
                    {
                        await EnsureConsumerGroupAsync(stoppingToken);
                        groupReady = true;
                        await RecoverPendingAsync(stoppingToken);
                        nextRecoveryAt = DateTimeOffset.UtcNow.Add(_settings.RecoveryInterval);
                    }

                    if (DateTimeOffset.UtcNow >= nextRecoveryAt)
                    {
                        await RecoverPendingAsync(stoppingToken);
                        nextRecoveryAt = DateTimeOffset.UtcNow.Add(_settings.RecoveryInterval);
                    }

                    await ProcessNewMessagesAsync(_settings.BlockDuration, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP", StringComparison.OrdinalIgnoreCase))
                {
                    groupReady = false;
                    _logger.LogWarning(ex, "Notification consumer group was removed and will be recreated.");
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(ex, "Redis notification relay is unavailable; retrying.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Redis notification relay iteration failed; retrying.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        finally
        {
            if (groupReady)
            {
                try
                {
                    var consumers = await _database.StreamConsumerInfoAsync(
                        _settings.StreamKey,
                        _settings.ConsumerGroup);
                    var hasPendingMessages = consumers.Any(consumer =>
                        consumer.Name == _consumerName &&
                        consumer.PendingMessageCount > 0);
                    if (!hasPendingMessages)
                    {
                        await _database.StreamDeleteConsumerAsync(
                            _settings.StreamKey,
                            _settings.ConsumerGroup,
                            _consumerName);
                    }
                }
                catch (RedisException ex)
                {
                    _logger.LogDebug(ex, "Unable to remove notification stream consumer metadata during shutdown.");
                }
            }
        }
    }

    internal async Task EnsureConsumerGroupAsync(CancellationToken ct = default)
    {
        try
        {
            await _database.StreamCreateConsumerGroupAsync(
                _settings.StreamKey,
                _settings.ConsumerGroup,
                "0-0",
                createStream: true).WaitAsync(ct);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            // Another API replica created the shared consumer group first.
        }
    }

    internal async Task<int> ProcessNewMessagesAsync(TimeSpan? block, CancellationToken ct = default)
    {
        var entries = await _database.StreamReadGroupAsync(
            _settings.StreamKey,
            _settings.ConsumerGroup,
            _consumerName,
            ">",
            _settings.BatchSize,
            noAck: false,
            claimMinIdleTime: null).WaitAsync(ct);

        foreach (var entry in entries)
            await ProcessEntryAsync(entry, ct);
        if (entries.Length > 0)
            await TrimAcknowledgedAsync(ct);
        if (entries.Length == 0 && block.HasValue)
            await Task.Delay(block.Value, ct);
        return entries.Length;
    }

    internal async Task<int> RecoverPendingAsync(CancellationToken ct = default)
    {
        var recovered = 0;
        RedisValue nextStartId = "0-0";
        do
        {
            var result = await _database.StreamAutoClaimAsync(
                _settings.StreamKey,
                _settings.ConsumerGroup,
                _consumerName,
                (long)_settings.ClaimIdleTime.TotalMilliseconds,
                nextStartId,
                _settings.BatchSize).WaitAsync(ct);

            foreach (var entry in result.ClaimedEntries)
            {
                await ProcessEntryAsync(entry, ct);
                recovered++;
            }

            if (result.ClaimedEntries.Length > 0)
                await TrimAcknowledgedAsync(ct);

            nextStartId = result.NextStartId;
        }
        while (nextStartId != "0-0" && !ct.IsCancellationRequested);

        await RemoveInactiveConsumersAsync(ct);
        return recovered;
    }

    private async Task RemoveInactiveConsumersAsync(CancellationToken ct)
    {
        var consumers = await _database.StreamConsumerInfoAsync(
            _settings.StreamKey,
            _settings.ConsumerGroup).WaitAsync(ct);
        var idleThreshold = Math.Max(
            TimeSpan.FromMinutes(5).TotalMilliseconds,
            Math.Max(
                _settings.ClaimIdleTime.TotalMilliseconds * 2,
                _settings.RecoveryInterval.TotalMilliseconds * 2));
        foreach (var consumer in consumers.Where(consumer =>
                     consumer.Name != _consumerName &&
                     consumer.PendingMessageCount == 0 &&
                     consumer.IdleTimeInMilliseconds >= idleThreshold))
        {
            await _database.StreamDeleteConsumerAsync(
                _settings.StreamKey,
                _settings.ConsumerGroup,
                consumer.Name).WaitAsync(ct);
        }
    }

    internal async Task<bool> ProcessEntryAsync(StreamEntry entry, CancellationToken ct = default)
    {
        if (!HubNotificationEnvelope.TryParse(entry, out var envelope, out var parseError))
        {
            await DeadLetterAndAcknowledgeAsync(entry, parseError, ct);
            return false;
        }

        if (!TryCreateDispatch(envelope!, out var dispatch, out var dispatchError))
        {
            await DeadLetterAndAcknowledgeAsync(entry, dispatchError, ct);
            return false;
        }

        try
        {
            var deliveredKey = (RedisKey)$"{_settings.StreamKey}:delivered:{envelope!.EventId:N}";
            if (await _database.KeyExistsAsync(deliveredKey).WaitAsync(ct))
            {
                await AcknowledgeAsync(entry.Id, ct);
                return true;
            }

            var orderingKey = StatefulOrderingKey(envelope);
            var processingKey = (RedisKey)(orderingKey is null
                ? $"{_settings.StreamKey}:processing:{envelope.EventId:N}"
                : $"{_settings.StreamKey}:processing-state:{orderingKey}");
            var processingOwner = Guid.NewGuid().ToString("N");
            var processingTtl = TimeSpan.FromMilliseconds(Math.Max(
                _settings.ClaimIdleTime.TotalMilliseconds * 2,
                TimeSpan.FromMinutes(1).TotalMilliseconds));
            if (!await _database.StringSetAsync(
                    processingKey,
                    processingOwner,
                    processingTtl,
                    When.NotExists).WaitAsync(ct))
            {
                return false;
            }

            using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var renewalTask = RenewProcessingLockAsync(
                processingKey,
                processingOwner,
                processingTtl,
                lockCts);
            try
            {
                if (await _database.KeyExistsAsync(deliveredKey).WaitAsync(ct))
                {
                    await AcknowledgeAsync(entry.Id, ct);
                    return true;
                }

                RedisKey watermarkKey = default;
                if (orderingKey is not null)
                {
                    watermarkKey = $"{_settings.StreamKey}:watermark:{orderingKey}";
                    var watermark = await _database.StringGetAsync(watermarkKey).WaitAsync(ct);
                    if (!watermark.IsNullOrEmpty && CompareStreamIds(entry.Id, watermark) <= 0)
                    {
                        await AcknowledgeAsync(entry.Id, ct);
                        return true;
                    }
                }

                await dispatch!(_target, lockCts.Token);
                lockCts.Token.ThrowIfCancellationRequested();
                if (orderingKey is not null)
                {
                    await _database.ScriptEvaluateAsync(
                        AdvanceWatermarkScript,
                        [watermarkKey],
                        [entry.Id, (long)_settings.DeliveryMarkerTtl.TotalMilliseconds]).WaitAsync(ct);
                }
                await _database.StringSetAsync(
                    deliveredKey,
                    "1",
                    _settings.DeliveryMarkerTtl).WaitAsync(ct);
                await AcknowledgeAsync(entry.Id, ct);
                return true;
            }
            finally
            {
                try
                {
                    await lockCts.CancelAsync();
                    await renewalTask;
                }
                finally
                {
                    await _database.ScriptEvaluateAsync(
                        ReleaseProcessingLockScript,
                        [processingKey],
                        [processingOwner]);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Leave the entry pending. This consumer or another API replica can
            // reclaim it after ClaimIdleTime and retry the forward.
            _logger.LogWarning(
                ex,
                "Failed to relay hub notification {EventId} ({Type}); leaving it pending.",
                envelope!.EventId,
                envelope.Type);
            return false;
        }
    }

    private async Task RenewProcessingLockAsync(
        RedisKey key,
        RedisValue owner,
        TimeSpan ttl,
        CancellationTokenSource linkedCts)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, ttl.TotalMilliseconds / 3));
        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                await Task.Delay(interval, linkedCts.Token);
                var renewed = (long)await _database.ScriptEvaluateAsync(
                    RenewProcessingLockScript,
                    [key],
                    [owner, (long)ttl.TotalMilliseconds]).WaitAsync(linkedCts.Token);
                if (renewed != 0)
                    continue;

                await linkedCts.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // Normal completion or lease loss.
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Lost Redis notification processing lock {ProcessingKey}.", key);
            await linkedCts.CancelAsync();
        }
    }

    internal static bool TryCreateDispatch(
        HubNotificationEnvelope envelope,
        out Func<IHubNotificationRelayTarget, CancellationToken, Task>? dispatch,
        out string error)
    {
        dispatch = null;
        if (envelope.SchemaVersion != HubNotificationEnvelope.CurrentSchemaVersion)
        {
            error = $"unsupported schema version {envelope.SchemaVersion}";
            return false;
        }

        try
        {
            dispatch = envelope.Type switch
            {
                HubNotificationTypes.ScoreUpdate => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, ScoreUpdateNotification _, CancellationToken ct) =>
                        target.NotifyLatestLeaderboardSnapshotAsync(envelope.CompetitionId, ct)),
                HubNotificationTypes.LeaderboardSnapshot => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, LeaderboardSnapshotNotification payload, CancellationToken ct) =>
                        target.NotifyLeaderboardSnapshotAsync(envelope.CompetitionId, payload.Entries, ct)),
                HubNotificationTypes.LeaderboardRefresh => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, LeaderboardRefreshNotification _, CancellationToken ct) =>
                        target.NotifyLatestLeaderboardSnapshotAsync(envelope.CompetitionId, ct)),
                HubNotificationTypes.FlagSolved => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, FlagSolvedNotification payload, CancellationToken ct) =>
                        target.NotifyFlagSolvedAsync(envelope.CompetitionId, payload.ChallengeId, payload.ChallengeName, payload.TeamId, payload.TeamName, payload.IsFirstBlood, ct)),
                HubNotificationTypes.ChallengeUpdate => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, ChallengeUpdateNotification payload, CancellationToken ct) =>
                        target.NotifyChallengeUpdateAsync(envelope.CompetitionId, payload.ChallengeId, payload.ChallengeName, payload.Action, ct)),
                HubNotificationTypes.CompetitionStateChanged => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, CompetitionStateChangedNotification payload, CancellationToken ct) =>
                        target.NotifyCompetitionStateChangeAsync(envelope.CompetitionId, payload.State, ct)),
                HubNotificationTypes.SubmissionEvent => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, SubmissionEventNotification payload, CancellationToken ct) =>
                        target.NotifySubmissionEventAsync(envelope.CompetitionId, payload.SubmissionId, payload.TeamId, payload.TeamName, payload.ChallengeId, payload.ChallengeName, payload.IsCorrect, ct)),
                HubNotificationTypes.ContainerEvent => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, ContainerEventNotification payload, CancellationToken ct) =>
                        target.NotifyContainerEventAsync(envelope.CompetitionId, payload.ContainerId, payload.ChallengeId, payload.TeamId, payload.EventType, ct)),
                HubNotificationTypes.SystemAlert => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, SystemAlertNotification payload, CancellationToken ct) =>
                        target.NotifySystemAlertAsync(envelope.CompetitionId, payload.Level, payload.Message, ct)),
                HubNotificationTypes.RoundStarted => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, RoundStartedNotification payload, CancellationToken ct) =>
                        target.NotifyRoundStartedAsync(envelope.CompetitionId, payload.RoundNumber, ct)),
                HubNotificationTypes.AttackLog => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, AttackLogNotification payload, CancellationToken ct) =>
                        target.NotifyAttackLogAsync(
                            envelope.CompetitionId,
                            payload.AttackerTeamId,
                            payload.AttackerTeamName,
                            payload.VictimTeamId,
                            payload.VictimTeamName,
                            payload.ChallengeId,
                            payload.ChallengeName,
                            payload.RoundNumber,
                            ct)),
                HubNotificationTypes.KohUpdate => Create(
                    envelope.PayloadJson,
                    (IHubNotificationRelayTarget target, KohUpdateNotification payload, CancellationToken ct) =>
                        target.NotifyKohUpdateAsync(envelope.CompetitionId, payload.ChallengeId, payload.ControllerTeamId, payload.Timestamp, ct)),
                _ => null
            };

            if (dispatch is null)
            {
                error = $"unsupported notification type '{envelope.Type}'";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"invalid payload for '{envelope.Type}': {ex.Message}";
            return false;
        }
    }

    private static Func<IHubNotificationRelayTarget, CancellationToken, Task> Create<TPayload>(
        string payloadJson,
        Func<IHubNotificationRelayTarget, TPayload, CancellationToken, Task> forward)
    {
        var payload = JsonSerializer.Deserialize<TPayload>(
            payloadJson,
            HubNotificationEnvelope.JsonOptions)
            ?? throw new JsonException("Payload was null.");
        return (target, ct) => forward(target, payload, ct);
    }

    internal static string? StatefulOrderingKey(HubNotificationEnvelope envelope)
        => envelope.Type switch
        {
            HubNotificationTypes.ScoreUpdate or
            HubNotificationTypes.LeaderboardSnapshot or
            HubNotificationTypes.LeaderboardRefresh => $"leaderboard:{envelope.CompetitionId:N}",
            HubNotificationTypes.CompetitionStateChanged => $"competition-state:{envelope.CompetitionId:N}",
            HubNotificationTypes.ChallengeUpdate => EntityOrderingKey<ChallengeUpdateNotification>(
                "challenge-state",
                envelope,
                payload => payload.ChallengeId),
            HubNotificationTypes.RoundStarted => $"round-state:{envelope.CompetitionId:N}",
            HubNotificationTypes.KohUpdate => EntityOrderingKey<KohUpdateNotification>(
                "koh-state",
                envelope,
                payload => payload.ChallengeId),
            _ => null
        };

    private static string EntityOrderingKey<TPayload>(
        string prefix,
        HubNotificationEnvelope envelope,
        Func<TPayload, Guid> entityId)
    {
        var payload = JsonSerializer.Deserialize<TPayload>(
            envelope.PayloadJson,
            HubNotificationEnvelope.JsonOptions)
            ?? throw new JsonException("Stateful notification payload was null.");
        return $"{prefix}:{envelope.CompetitionId:N}:{entityId(payload):N}";
    }

    private static int CompareStreamIds(RedisValue left, RedisValue right)
    {
        static (long Milliseconds, long Sequence)? Parse(RedisValue value)
        {
            var parts = value.ToString().Split('-', 2);
            return parts.Length == 2 &&
                   long.TryParse(parts[0], out var milliseconds) &&
                   long.TryParse(parts[1], out var sequence)
                ? (milliseconds, sequence)
                : null;
        }

        var leftId = Parse(left);
        var rightId = Parse(right);
        if (leftId is null || rightId is null)
            return string.CompareOrdinal(left.ToString(), right.ToString());

        var milliseconds = leftId.Value.Milliseconds.CompareTo(rightId.Value.Milliseconds);
        return milliseconds != 0
            ? milliseconds
            : leftId.Value.Sequence.CompareTo(rightId.Value.Sequence);
    }

    private async Task DeadLetterAndAcknowledgeAsync(StreamEntry entry, string reason, CancellationToken ct)
    {
        var rawValues = JsonSerializer.Serialize(
            entry.Values.Select(value => new
            {
                name = value.Name.ToString(),
                value = value.Value.ToString()
            }),
            HubNotificationEnvelope.JsonOptions);
        var deadLetterValues = new NameValueEntry[]
        {
            new("originalStreamId", entry.Id),
            new("reason", reason),
            new("values", rawValues),
            new("failedAt", DateTimeOffset.UtcNow.ToString("O"))
        };

        await _database.StreamAddAsync(
            _settings.DeadLetterStreamKey,
            deadLetterValues,
            maxLength: _settings.DeadLetterMaxLength,
            useApproximateMaxLength: true).WaitAsync(ct);
        await AcknowledgeAsync(entry.Id, ct);
        _logger.LogWarning(
            "Moved malformed hub notification {StreamId} to {DeadLetterStream}: {Reason}",
            entry.Id,
            _settings.DeadLetterStreamKey,
            reason);
    }

    private Task AcknowledgeAsync(RedisValue entryId, CancellationToken ct)
        => _database.StreamAcknowledgeAsync(
            _settings.StreamKey,
            _settings.ConsumerGroup,
            entryId).WaitAsync(ct);

    private async Task TrimAcknowledgedAsync(CancellationToken ct)
    {
        var pending = await _database.StreamPendingAsync(
            _settings.StreamKey,
            _settings.ConsumerGroup).WaitAsync(ct);
        RedisValue minimumRetainedId;
        if (pending.PendingMessageCount > 0)
        {
            minimumRetainedId = pending.LowestPendingMessageId;
        }
        else
        {
            var group = (await _database.StreamGroupInfoAsync(_settings.StreamKey).WaitAsync(ct))
                .FirstOrDefault(info => info.Name == _settings.ConsumerGroup);
            minimumRetainedId = string.IsNullOrEmpty(group.Name) ? RedisValue.Null : group.LastDeliveredId;
        }

        if (!minimumRetainedId.IsNullOrEmpty && minimumRetainedId != "0-0")
        {
            await _database.StreamTrimByMinIdAsync(
                _settings.StreamKey,
                minimumRetainedId,
                useApproximateMaxLength: false).WaitAsync(ct);
        }
    }
}

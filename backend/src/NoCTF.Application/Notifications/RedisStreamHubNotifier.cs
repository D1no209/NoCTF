using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace NoCTF.Application.Notifications;

public static class HubNotificationTypes
{
    public const string ScoreUpdate = "score.update";
    public const string LeaderboardSnapshot = "leaderboard.snapshot";
    public const string LeaderboardRefresh = "leaderboard.refresh";
    public const string FlagSolved = "flag.solved";
    public const string ChallengeUpdate = "challenge.update";
    public const string CompetitionStateChanged = "competition.state-changed";
    public const string SubmissionEvent = "submission.event";
    public const string ContainerEvent = "container.event";
    public const string SystemAlert = "system.alert";
    public const string RoundStarted = "round.started";
    public const string AttackLog = "attack.log";
    public const string KohUpdate = "koh.update";
}

public sealed record ScoreUpdateNotification(Guid TeamId, string TeamName, long NewScore, int NewRank);
public sealed record LeaderboardSnapshotNotification(LeaderboardEntryPayload[] Entries);
public sealed record LeaderboardRefreshNotification;
public sealed record FlagSolvedNotification(Guid ChallengeId, string ChallengeName, Guid TeamId, string TeamName, bool IsFirstBlood);
public sealed record ChallengeUpdateNotification(Guid ChallengeId, string ChallengeName, string Action);
public sealed record CompetitionStateChangedNotification(string State);
public sealed record SubmissionEventNotification(Guid SubmissionId, Guid TeamId, string TeamName, Guid ChallengeId, string ChallengeName, bool IsCorrect);
public sealed record ContainerEventNotification(Guid ContainerId, Guid ChallengeId, Guid TeamId, string EventType);
public sealed record SystemAlertNotification(string Level, string Message);
public sealed record RoundStartedNotification(int RoundNumber);
public sealed record AttackLogNotification(Guid AttackerTeamId, string AttackerTeamName, Guid VictimTeamId, string VictimTeamName, Guid ChallengeId, string ChallengeName, int RoundNumber);
public sealed record KohUpdateNotification(Guid ChallengeId, Guid? ControllerTeamId, DateTime Timestamp);

public sealed record HubNotificationEnvelope(
    Guid EventId,
    int SchemaVersion,
    string Type,
    Guid CompetitionId,
    string PayloadJson,
    DateTimeOffset CreatedAt)
{
    public const int CurrentSchemaVersion = 1;

    public NameValueEntry[] ToStreamValues()
        =>
        [
            new("eventId", EventId.ToString("N")),
            new("schemaVersion", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
            new("type", Type),
            new("competitionId", CompetitionId.ToString("N")),
            new("payload", PayloadJson),
            new("createdAt", CreatedAt.ToString("O", CultureInfo.InvariantCulture))
        ];

    public static HubNotificationEnvelope Create<T>(string type, Guid competitionId, T payload)
        => new(
            Guid.NewGuid(),
            CurrentSchemaVersion,
            type,
            competitionId,
            JsonSerializer.Serialize(payload, JsonOptions),
            DateTimeOffset.UtcNow);

    public static bool TryParse(StreamEntry entry, out HubNotificationEnvelope? envelope, out string error)
    {
        envelope = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in entry.Values)
            values[pair.Name.ToString()] = pair.Value.ToString();

        if (!values.TryGetValue("eventId", out var eventIdValue) ||
            !Guid.TryParseExact(eventIdValue, "N", out var eventId))
        {
            error = "eventId is missing or invalid";
            return false;
        }

        if (!values.TryGetValue("schemaVersion", out var schemaVersionValue) ||
            !int.TryParse(schemaVersionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var schemaVersion))
        {
            error = "schemaVersion is missing or invalid";
            return false;
        }

        if (!values.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
        {
            error = "type is missing";
            return false;
        }

        if (!values.TryGetValue("competitionId", out var competitionIdValue) ||
            !Guid.TryParseExact(competitionIdValue, "N", out var competitionId))
        {
            error = "competitionId is missing or invalid";
            return false;
        }

        if (!values.TryGetValue("payload", out var payloadJson) || string.IsNullOrWhiteSpace(payloadJson))
        {
            error = "payload is missing";
            return false;
        }

        if (!values.TryGetValue("createdAt", out var createdAtValue) ||
            !DateTimeOffset.TryParseExact(
                createdAtValue,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt))
        {
            error = "createdAt is missing or invalid";
            return false;
        }

        envelope = new HubNotificationEnvelope(
            eventId,
            schemaVersion,
            type,
            competitionId,
            payloadJson,
            createdAt);
        error = string.Empty;
        return true;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed record HubNotificationStreamSettings(
    string StreamKey,
    string DeadLetterStreamKey,
    string ConsumerGroup,
    int MaxLength,
    int DeadLetterMaxLength,
    int BatchSize,
    int MaxPayloadBytes,
    TimeSpan BlockDuration,
    TimeSpan ClaimIdleTime,
    TimeSpan RecoveryInterval,
    TimeSpan DeliveryMarkerTtl)
{
    public static HubNotificationStreamSettings FromConfiguration(IConfiguration configuration)
    {
        const string section = "Notifications:RedisStream";
        // MaxLength remains in the settings contract for configuration and
        // binary compatibility. It must never be passed to XADD because Redis
        // can trim entries that are still pending in a consumer group.
        return new HubNotificationStreamSettings(
            configuration[$"{section}:Key"] ?? "noctf:hub-notifications:v1",
            configuration[$"{section}:DeadLetterKey"] ?? "noctf:hub-notifications:dead-letter:v1",
            configuration[$"{section}:ConsumerGroup"] ?? "noctf-api-relays-v1",
            Math.Clamp(configuration.GetValue($"{section}:MaxLength", 100_000), 1_000, 1_000_000),
            Math.Clamp(configuration.GetValue($"{section}:DeadLetterMaxLength", 10_000), 100, 100_000),
            Math.Clamp(configuration.GetValue($"{section}:BatchSize", 50), 1, 250),
            Math.Clamp(configuration.GetValue($"{section}:MaxPayloadBytes", 65_536), 1_024, 1_048_576),
            TimeSpan.FromMilliseconds(Math.Clamp(configuration.GetValue($"{section}:BlockMilliseconds", 2_000), 100, 10_000)),
            TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue($"{section}:ClaimIdleSeconds", 30), 1, 3_600)),
            TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue($"{section}:RecoveryIntervalSeconds", 30), 1, 3_600)),
            TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue($"{section}:DeliveryMarkerTtlSeconds", 86_400), 60, 604_800)));
    }
}

/// <summary>
/// Worker-side notification adapter. Events are persisted to a Redis Stream
/// and later relayed by one API replica. The relay trims only acknowledged
/// entries so a backlog is never truncated while consumers are unavailable.
/// </summary>
public sealed class RedisStreamHubNotifier : IHubNotifierService
{
    private readonly Func<RedisKey, NameValueEntry[], CancellationToken, Task> _append;
    private readonly HubNotificationStreamSettings _settings;
    private readonly ILogger<RedisStreamHubNotifier>? _logger;

    public RedisStreamHubNotifier(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisStreamHubNotifier>? logger = null)
        : this(
            configuration,
            CreateAppender(redis),
            logger)
    {
    }

    internal RedisStreamHubNotifier(
        IConfiguration configuration,
        Func<RedisKey, NameValueEntry[], CancellationToken, Task> append,
        ILogger<RedisStreamHubNotifier>? logger = null)
    {
        _append = append;
        _settings = HubNotificationStreamSettings.FromConfiguration(configuration);
        _logger = logger;
    }

    private static Func<RedisKey, NameValueEntry[], CancellationToken, Task> CreateAppender(
        IConnectionMultiplexer redis)
    {
        var database = redis.GetDatabase();
        return async (key, values, ct) =>
        {
            await database.StreamAddAsync(key, values).WaitAsync(ct);
        };
    }

    public Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default)
        // The versioned cache is authoritative. Publishing a full-state refresh
        // keeps out-of-order relay processing from dropping a snapshot in favor
        // of a newer, but incomplete, per-team delta.
        => PublishAsync(HubNotificationTypes.LeaderboardRefresh, competitionId, new LeaderboardRefreshNotification(), ct);

    public Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default)
        // The versioned snapshot is already in Redis. Keep the durable stream
        // message small and let the API relay load the newest snapshot.
        => PublishAsync(HubNotificationTypes.LeaderboardRefresh, competitionId, new LeaderboardRefreshNotification(), ct);

    public Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.FlagSolved, competitionId, new FlagSolvedNotification(challengeId, challengeName, teamId, teamName, isFirstBlood), ct);

    public Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.ChallengeUpdate, competitionId, new ChallengeUpdateNotification(challengeId, challengeName, action), ct);

    public Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.CompetitionStateChanged, competitionId, new CompetitionStateChangedNotification(state), ct);

    public Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.SubmissionEvent, competitionId, new SubmissionEventNotification(submissionId, teamId, teamName, challengeId, challengeName, isCorrect), ct);

    public Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.ContainerEvent, competitionId, new ContainerEventNotification(containerId, challengeId, teamId, eventType), ct);

    public Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.SystemAlert, competitionId, new SystemAlertNotification(level, message), ct);

    public Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.RoundStarted, competitionId, new RoundStartedNotification(roundNumber), ct);

    public Task NotifyAttackLogAsync(
        Guid competitionId,
        Guid attackerTeamId,
        string attackerTeamName,
        Guid victimTeamId,
        string victimTeamName,
        Guid challengeId,
        string challengeName,
        int roundNumber,
        CancellationToken ct = default)
        => PublishAsync(
            HubNotificationTypes.AttackLog,
            competitionId,
            new AttackLogNotification(
                attackerTeamId,
                attackerTeamName,
                victimTeamId,
                victimTeamName,
                challengeId,
                challengeName,
                roundNumber),
            ct);

    public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default)
        => PublishAsync(HubNotificationTypes.KohUpdate, competitionId, new KohUpdateNotification(challengeId, controllerTeamId, timestamp), ct);

    private async Task PublishAsync<T>(string type, Guid competitionId, T payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var envelope = HubNotificationEnvelope.Create(type, competitionId, payload);
        var payloadBytes = Encoding.UTF8.GetByteCount(envelope.PayloadJson);
        if (payloadBytes > _settings.MaxPayloadBytes)
        {
            _logger?.LogWarning(
                "Rejecting oversized hub notification {Type} for competition {CompetitionId}: {PayloadBytes} bytes exceeds {MaxPayloadBytes}.",
                type,
                competitionId,
                payloadBytes,
                _settings.MaxPayloadBytes);
            throw new InvalidOperationException(
                $"Hub notification payload exceeds the configured {_settings.MaxPayloadBytes} byte limit.");
        }
        await _append(_settings.StreamKey, envelope.ToStreamValues(), ct);
    }
}

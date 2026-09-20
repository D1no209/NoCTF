using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.NoCtf;

namespace NoCTF.Bot.Persistence;

public sealed class BotStateStore(IOptions<RelayOptions> options, TimeProvider timeProvider)
    : IDisposable
{
    private const int CommandTimeoutSeconds = 15;
    private readonly Lock writeLock = new();
    private readonly string connectionString = CreateConnectionString(options.Value.StatePath);

    public void Initialize()
    {
        var path = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        using var connection = OpenConnection();
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=FULL;");
        Execute(connection, "PRAGMA foreign_keys=ON;");
        Execute(connection, "PRAGMA busy_timeout=15000;");
        Execute(connection, Schema);
        RecoverInterruptedOutboundMessages(connection);
    }

    public GroupSubscription? GetSubscription(long groupId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT group_id, competition_id, broadcast_enabled, scoreboard_enabled,
                   blood_enabled, suspended_reason, updated_at
            FROM group_subscriptions
            WHERE group_id = $groupId
            """;
        command.Parameters.AddWithValue("$groupId", groupId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSubscription(reader) : null;
    }

    public IReadOnlyList<GroupSubscription> GetSubscriptions(Guid? competitionId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = competitionId is null
            ? """
                SELECT group_id, competition_id, broadcast_enabled, scoreboard_enabled,
                       blood_enabled, suspended_reason, updated_at
                FROM group_subscriptions
                ORDER BY group_id
                """
            : """
                SELECT group_id, competition_id, broadcast_enabled, scoreboard_enabled,
                       blood_enabled, suspended_reason, updated_at
                FROM group_subscriptions
                WHERE competition_id = $competitionId
                ORDER BY group_id
                """;
        if (competitionId is not null)
            command.Parameters.AddWithValue("$competitionId", competitionId.Value.ToString("D"));
        using var reader = command.ExecuteReader();
        var items = new List<GroupSubscription>();
        while (reader.Read()) items.Add(ReadSubscription(reader));
        return items;
    }

    public void UpsertSubscription(long groupId, Guid competitionId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE group_subscriptions
                SET competition_id = $competitionId,
                    suspended_reason = NULL,
                    updated_at = $updatedAt
                WHERE group_id = $groupId
                """;
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
            command.Parameters.AddWithValue("$updatedAt", NowText());
            var updated = command.ExecuteNonQuery();
            if (updated == 0)
            {
                command.CommandText = """
                    INSERT OR IGNORE INTO group_subscriptions(
                        group_id, competition_id, broadcast_enabled, scoreboard_enabled,
                        blood_enabled, suspended_reason, updated_at)
                    VALUES($groupId, $competitionId, 1, 1, 1, NULL, $updatedAt)
                    """;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public bool DeleteSubscription(long groupId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM group_subscriptions WHERE group_id = $groupId";
            command.Parameters.AddWithValue("$groupId", groupId);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public bool SetSubscriptionOption(long groupId, string column, bool enabled)
    {
        if (column is not ("broadcast_enabled" or "scoreboard_enabled" or "blood_enabled"))
            throw new ArgumentOutOfRangeException(nameof(column));
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE group_subscriptions
                SET {column} = $enabled, updated_at = $updatedAt
                WHERE group_id = $groupId
                """;
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$groupId", groupId);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public void CancelPendingOutbound(long groupId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE outbound_messages
                SET state = $deadLetter,
                    last_error = 'subscription_changed',
                    updated_at = $updatedAt
                WHERE group_id = $groupId AND state = $pending
                """;
            command.Parameters.AddWithValue("$deadLetter", (int)OutboundMessageState.DeadLetter);
            command.Parameters.AddWithValue("$pending", (int)OutboundMessageState.Pending);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$groupId", groupId);
            command.ExecuteNonQuery();
        }
    }

    public void SuspendCompetition(Guid competitionId, string reason)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE group_subscriptions
                SET suspended_reason = $reason, updated_at = $updatedAt
                WHERE competition_id = $competitionId
                """;
            command.Parameters.AddWithValue("$reason", reason);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
            command.ExecuteNonQuery();
        }
    }

    public bool TryRecordInbound(string scene, long peerId, long messageSequence)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO inbound_messages(scene, peer_id, message_sequence, received_at)
                VALUES($scene, $peerId, $messageSequence, $receivedAt)
                """;
            command.Parameters.AddWithValue("$scene", scene);
            command.Parameters.AddWithValue("$peerId", peerId);
            command.Parameters.AddWithValue("$messageSequence", messageSequence);
            command.Parameters.AddWithValue("$receivedAt", NowText());
            return command.ExecuteNonQuery() > 0;
        }
    }

    public CompetitionSnapshot? GetCompetitionSnapshot(Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT competition_id, status, start_time, end_time, content_hash, updated_at
            FROM competition_snapshots
            WHERE competition_id = $competitionId
            """;
        command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                Guid.Parse(reader.GetString(0)),
                Enum.Parse<CompetitionStatus>(reader.GetString(1)),
                ParseTime(reader.GetString(2)),
                ParseTime(reader.GetString(3)),
                reader.GetString(4),
                ParseTime(reader.GetString(5)))
            : null;
    }

    public void SaveCompetitionSnapshot(CompetitionSnapshot snapshot)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO competition_snapshots(
                    competition_id, status, start_time, end_time, content_hash, updated_at)
                VALUES($competitionId, $status, $startTime, $endTime, $contentHash, $updatedAt)
                """;
            command.Parameters.AddWithValue("$competitionId", snapshot.CompetitionId.ToString("D"));
            command.Parameters.AddWithValue("$status", snapshot.Status.ToString());
            command.Parameters.AddWithValue("$startTime", TimeText(snapshot.StartTime));
            command.Parameters.AddWithValue("$endTime", TimeText(snapshot.EndTime));
            command.Parameters.AddWithValue("$contentHash", snapshot.ContentHash);
            command.Parameters.AddWithValue("$updatedAt", TimeText(snapshot.UpdatedAt));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyDictionary<Guid, ChallengeSnapshot> GetChallengeSnapshots(Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT competition_id, challenge_id, title, published, content_hash
            FROM challenge_snapshots
            WHERE competition_id = $competitionId
            """;
        command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
        using var reader = command.ExecuteReader();
        var items = new Dictionary<Guid, ChallengeSnapshot>();
        while (reader.Read())
        {
            var item = new ChallengeSnapshot(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4));
            items[item.ChallengeId] = item;
        }
        return items;
    }

    public void ReplaceChallengeSnapshots(
        Guid competitionId,
        IReadOnlyCollection<ChallengeSnapshot> snapshots)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM challenge_snapshots WHERE competition_id = $competitionId";
                delete.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                delete.ExecuteNonQuery();
            }
            foreach (var snapshot in snapshots)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO challenge_snapshots(
                        competition_id, challenge_id, title, published, content_hash)
                    VALUES($competitionId, $challengeId, $title, $published, $contentHash)
                    """;
                insert.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                insert.Parameters.AddWithValue("$challengeId", snapshot.ChallengeId.ToString("D"));
                insert.Parameters.AddWithValue("$title", snapshot.Title);
                insert.Parameters.AddWithValue("$published", snapshot.Published ? 1 : 0);
                insert.Parameters.AddWithValue("$contentHash", snapshot.ContentHash);
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public IReadOnlyDictionary<Guid, TeamSnapshot> GetTeamSnapshots(Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT competition_id, team_id, team_name, rank, score,
                   achievement_hash, achievements_json
            FROM team_snapshots
            WHERE competition_id = $competitionId
            """;
        command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
        using var reader = command.ExecuteReader();
        var items = new Dictionary<Guid, TeamSnapshot>();
        while (reader.Read())
        {
            var item = new TeamSnapshot(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.GetString(6));
            items[item.TeamId] = item;
        }
        return items;
    }

    public void ReplaceTeamSnapshots(
        Guid competitionId,
        IReadOnlyCollection<TeamSnapshot> snapshots)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM team_snapshots WHERE competition_id = $competitionId";
                delete.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                delete.ExecuteNonQuery();
            }
            foreach (var snapshot in snapshots)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO team_snapshots(
                        competition_id, team_id, team_name, rank, score,
                        achievement_hash, achievements_json)
                    VALUES($competitionId, $teamId, $teamName, $rank, $score,
                           $achievementHash, $achievementsJson)
                    """;
                insert.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                insert.Parameters.AddWithValue("$teamId", snapshot.TeamId.ToString("D"));
                insert.Parameters.AddWithValue("$teamName", snapshot.TeamName);
                insert.Parameters.AddWithValue("$rank", snapshot.Rank is null ? DBNull.Value : snapshot.Rank.Value);
                insert.Parameters.AddWithValue("$score", snapshot.Score);
                insert.Parameters.AddWithValue("$achievementHash", snapshot.AchievementHash);
                insert.Parameters.AddWithValue("$achievementsJson", snapshot.AchievementsJson);
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public bool EnqueueOutbound(string dedupeKey, long groupId, string payload)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO outbound_messages(
                    id, dedupe_key, group_id, payload, state, attempt_count,
                    next_attempt_at, last_error, created_at, updated_at)
                VALUES($id, $dedupeKey, $groupId, $payload, $state, 0,
                       $nextAttemptAt, NULL, $createdAt, $updatedAt)
                """;
            var now = NowText();
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$dedupeKey", dedupeKey);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$state", (int)OutboundMessageState.Pending);
            command.Parameters.AddWithValue("$nextAttemptAt", now);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            return command.ExecuteNonQuery() > 0;
        }
    }

    public OutboundMessage? ClaimDueOutbound()
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            OutboundMessage? item;
            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = """
                    SELECT id, dedupe_key, group_id, payload, state, attempt_count, next_attempt_at
                    FROM outbound_messages
                    WHERE state = $pending AND next_attempt_at <= $now
                    ORDER BY next_attempt_at, created_at
                    LIMIT 1
                    """;
                select.Parameters.AddWithValue("$pending", (int)OutboundMessageState.Pending);
                select.Parameters.AddWithValue("$now", NowText());
                using var reader = select.ExecuteReader();
                item = reader.Read() ? ReadOutbound(reader) : null;
            }
            if (item is null)
            {
                transaction.Commit();
                return null;
            }
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE outbound_messages
                SET state = $sending, attempt_count = attempt_count + 1, updated_at = $updatedAt
                WHERE id = $id AND state = $pending
                """;
            update.Parameters.AddWithValue("$sending", (int)OutboundMessageState.Sending);
            update.Parameters.AddWithValue("$pending", (int)OutboundMessageState.Pending);
            update.Parameters.AddWithValue("$updatedAt", NowText());
            update.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return null;
            }
            transaction.Commit();
            return item with
            {
                State = OutboundMessageState.Sending,
                AttemptCount = item.AttemptCount + 1
            };
        }
    }

    public void CompleteOutbound(Guid id)
    {
        UpdateOutbound(id, OutboundMessageState.Completed, null, null);
    }

    public void FailOutbound(Guid id, int attemptCount, string safeError)
    {
        var delay = OutboundRetrySchedule.AfterFailure(attemptCount);
        UpdateOutbound(
            id,
            delay is null ? OutboundMessageState.DeadLetter : OutboundMessageState.Pending,
            delay is null ? null : timeProvider.GetUtcNow() + delay,
            safeError.Length <= 300 ? safeError : safeError[..300]);
    }

    private void UpdateOutbound(
        Guid id,
        OutboundMessageState state,
        DateTimeOffset? nextAttemptAt,
        string? safeError)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE outbound_messages
                SET state = $state,
                    next_attempt_at = COALESCE($nextAttemptAt, next_attempt_at),
                    last_error = $lastError,
                    updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$state", (int)state);
            command.Parameters.AddWithValue(
                "$nextAttemptAt",
                nextAttemptAt is null ? DBNull.Value : TimeText(nextAttemptAt.Value));
            command.Parameters.AddWithValue("$lastError", safeError is null ? DBNull.Value : safeError);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();
        }
    }

    private void RecoverInterruptedOutboundMessages(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbound_messages
            SET state = $pending, next_attempt_at = $now, updated_at = $now,
                last_error = 'process_interrupted_during_send'
            WHERE state = $sending
            """;
        command.Parameters.AddWithValue("$pending", (int)OutboundMessageState.Pending);
        command.Parameters.AddWithValue("$sending", (int)OutboundMessageState.Sending);
        command.Parameters.AddWithValue("$now", NowText());
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString) { DefaultTimeout = CommandTimeoutSeconds };
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.ExecuteNonQuery();
    }

    private static GroupSubscription ReadSubscription(SqliteDataReader reader) =>
        new(
            reader.GetInt64(0),
            Guid.Parse(reader.GetString(1)),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            ParseTime(reader.GetString(6)));

    private static OutboundMessage ReadOutbound(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            (OutboundMessageState)reader.GetInt32(4),
            reader.GetInt32(5),
            ParseTime(reader.GetString(6)));

    private static string CreateConnectionString(string statePath)
    {
        var fullPath = Path.GetFullPath(statePath);
        return new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = CommandTimeoutSeconds
        }.ToString();
    }

    private string NowText() => TimeText(timeProvider.GetUtcNow());

    private static string TimeText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public void Dispose() => SqliteConnection.ClearAllPools();

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS group_subscriptions (
            group_id INTEGER PRIMARY KEY,
            competition_id TEXT NOT NULL,
            broadcast_enabled INTEGER NOT NULL,
            scoreboard_enabled INTEGER NOT NULL,
            blood_enabled INTEGER NOT NULL,
            suspended_reason TEXT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_group_subscriptions_competition
            ON group_subscriptions(competition_id);

        CREATE TABLE IF NOT EXISTS competition_snapshots (
            competition_id TEXT PRIMARY KEY,
            status TEXT NOT NULL,
            start_time TEXT NOT NULL,
            end_time TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS challenge_snapshots (
            competition_id TEXT NOT NULL,
            challenge_id TEXT NOT NULL,
            title TEXT NOT NULL,
            published INTEGER NOT NULL,
            content_hash TEXT NOT NULL,
            PRIMARY KEY(competition_id, challenge_id)
        );

        CREATE TABLE IF NOT EXISTS team_snapshots (
            competition_id TEXT NOT NULL,
            team_id TEXT NOT NULL,
            team_name TEXT NOT NULL,
            rank INTEGER NULL,
            score INTEGER NOT NULL,
            achievement_hash TEXT NOT NULL,
            achievements_json TEXT NOT NULL,
            PRIMARY KEY(competition_id, team_id)
        );

        CREATE TABLE IF NOT EXISTS inbound_messages (
            scene TEXT NOT NULL,
            peer_id INTEGER NOT NULL,
            message_sequence INTEGER NOT NULL,
            received_at TEXT NOT NULL,
            PRIMARY KEY(scene, peer_id, message_sequence)
        );

        CREATE TABLE IF NOT EXISTS outbound_messages (
            id TEXT PRIMARY KEY,
            dedupe_key TEXT NOT NULL UNIQUE,
            group_id INTEGER NOT NULL,
            payload TEXT NOT NULL,
            state INTEGER NOT NULL,
            attempt_count INTEGER NOT NULL,
            next_attempt_at TEXT NOT NULL,
            last_error TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_outbound_messages_due
            ON outbound_messages(state, next_attempt_at, created_at);
        """;
}

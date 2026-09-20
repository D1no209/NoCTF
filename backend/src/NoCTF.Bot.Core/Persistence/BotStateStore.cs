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
    private const int CurrentSchemaVersion = 2;
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
        var version = ReadSchemaVersion(connection);
        if (version == 0)
        {
            if (TableExists(connection, "group_subscriptions"))
                MigrateLegacySchema(connection);
            else
                CreateCurrentSchema(connection);
        }
        else if (version != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported BOT state schema version {version}.");
        }
        RecoverInterruptedOutboundMessages(connection);
    }

    public GroupAccess? GetGroupAccess(string providerId, string groupId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider_id, group_id, master_authorized_at, enabled, updated_at
            FROM group_access
            WHERE provider_id = $providerId AND group_id = $groupId
            """;
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadGroupAccess(reader) : null;
    }

    public void AuthorizeAndEnableGroup(string providerId, string groupId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE group_access
                SET master_authorized_at = COALESCE(master_authorized_at, $now),
                    enabled = 1,
                    updated_at = $now
                WHERE provider_id = $providerId AND group_id = $groupId
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$now", NowText());
            if (command.ExecuteNonQuery() == 0)
            {
                command.CommandText = """
                    INSERT INTO group_access(
                        provider_id, group_id, master_authorized_at, enabled, updated_at)
                    VALUES($providerId, $groupId, $now, 1, $now)
                    """;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public bool SetGroupEnabled(string providerId, string groupId, bool enabled)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE group_access
                SET enabled = $enabled, updated_at = $updatedAt
                WHERE provider_id = $providerId AND group_id = $groupId
                  AND master_authorized_at IS NOT NULL
                """;
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void RevokeGroup(string providerId, string groupId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            ExecuteGroupDelete(connection, transaction, "announcement_checkpoints", providerId, groupId);
            ExecuteGroupDelete(connection, transaction, "group_admins", providerId, groupId);
            ExecuteGroupDelete(connection, transaction, "group_subscriptions", providerId, groupId);
            ExecuteGroupDelete(connection, transaction, "group_access", providerId, groupId);
            CancelPendingOutbound(
                connection,
                transaction,
                providerId,
                groupId,
                "group_revoked",
                NowText());
            transaction.Commit();
        }
    }

    public bool IsGroupAdmin(string providerId, string groupId, string userId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM group_admins
                WHERE provider_id = $providerId AND group_id = $groupId AND user_id = $userId)
            """;
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    public void AddGroupAdmin(
        string providerId,
        string groupId,
        string userId,
        string grantedBy)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO group_admins(
                    provider_id, group_id, user_id, granted_by, granted_at)
                VALUES($providerId, $groupId, $userId, $grantedBy, $grantedAt)
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$grantedBy", grantedBy);
            command.Parameters.AddWithValue("$grantedAt", NowText());
            command.ExecuteNonQuery();
        }
    }

    public bool RemoveGroupAdmin(string providerId, string groupId, string userId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM group_admins
                WHERE provider_id = $providerId AND group_id = $groupId AND user_id = $userId
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$userId", userId);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public IReadOnlyList<string> GetGroupAdmins(string providerId, string groupId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_id FROM group_admins
            WHERE provider_id = $providerId AND group_id = $groupId
            ORDER BY user_id
            """;
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        using var reader = command.ExecuteReader();
        var items = new List<string>();
        while (reader.Read()) items.Add(reader.GetString(0));
        return items;
    }

    public GroupSubscription? GetSubscription(string providerId, string groupId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SubscriptionSelect + """

            WHERE subscription.provider_id = $providerId
              AND subscription.group_id = $groupId
            """;
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSubscription(reader) : null;
    }

    public IReadOnlyList<GroupSubscription> GetSubscriptions(
        string providerId,
        Guid? competitionId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SubscriptionSelect + (competitionId is null
            ? "\nWHERE subscription.provider_id = $providerId ORDER BY subscription.group_id"
            : "\nWHERE subscription.provider_id = $providerId AND subscription.competition_id = $competitionId ORDER BY subscription.group_id");
        command.Parameters.AddWithValue("$providerId", providerId);
        if (competitionId is not null)
            command.Parameters.AddWithValue("$competitionId", competitionId.Value.ToString("D"));
        using var reader = command.ExecuteReader();
        var items = new List<GroupSubscription>();
        while (reader.Read()) items.Add(ReadSubscription(reader));
        return items;
    }

    public void UpsertSubscription(string providerId, string groupId, Guid competitionId)
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
                WHERE provider_id = $providerId AND group_id = $groupId
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
            command.Parameters.AddWithValue("$updatedAt", NowText());
            if (command.ExecuteNonQuery() == 0)
            {
                command.CommandText = """
                    INSERT INTO group_subscriptions(
                        provider_id, group_id, competition_id, broadcast_enabled,
                        scoreboard_enabled, blood_enabled, suspended_reason, updated_at)
                    VALUES($providerId, $groupId, $competitionId, 1, 1, 1, NULL, $updatedAt)
                    """;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public bool DeleteSubscription(string providerId, string groupId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM group_subscriptions
                WHERE provider_id = $providerId AND group_id = $groupId
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public bool SetSubscriptionOption(
        string providerId,
        string groupId,
        string column,
        bool enabled)
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
                WHERE provider_id = $providerId AND group_id = $groupId
                """;
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$updatedAt", NowText());
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public void CancelPendingOutbound(string providerId, string groupId, string reason = "group_changed")
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            CancelPendingOutbound(
                connection,
                transaction,
                providerId,
                groupId,
                reason,
                NowText());
            transaction.Commit();
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

    public bool TryRecordInbound(string providerId, string groupId, string messageId)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO inbound_messages(
                    provider_id, group_id, message_id, received_at)
                VALUES($providerId, $groupId, $messageId, $receivedAt)
                """;
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$messageId", messageId);
            command.Parameters.AddWithValue("$receivedAt", NowText());
            return command.ExecuteNonQuery() == 1;
        }
    }

    public AnnouncementCheckpoint? GetAnnouncementCheckpoint(
        string providerId,
        string groupId,
        Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT provider_id, group_id, competition_id, published_at, announcement_id
            FROM announcement_checkpoints
            WHERE provider_id = $providerId AND group_id = $groupId
              AND competition_id = $competitionId
            """;
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                reader.GetString(0),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                ParseTime(reader.GetString(3)),
                Guid.Parse(reader.GetString(4)))
            : null;
    }

    public void SaveAnnouncementCheckpoint(AnnouncementCheckpoint checkpoint)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO announcement_checkpoints(
                    provider_id, group_id, competition_id, published_at, announcement_id)
                VALUES($providerId, $groupId, $competitionId, $publishedAt, $announcementId)
                """;
            command.Parameters.AddWithValue("$providerId", checkpoint.ProviderId);
            command.Parameters.AddWithValue("$groupId", checkpoint.GroupId);
            command.Parameters.AddWithValue("$competitionId", checkpoint.CompetitionId.ToString("D"));
            command.Parameters.AddWithValue("$publishedAt", TimeText(checkpoint.PublishedAt));
            command.Parameters.AddWithValue("$announcementId", checkpoint.AnnouncementId.ToString("D"));
            command.ExecuteNonQuery();
        }
    }

    public CompetitionSnapshot? GetCompetitionSnapshot(Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT competition_id, status, start_time, end_time, content_hash, updated_at
            FROM competition_snapshots WHERE competition_id = $competitionId
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
            FROM challenge_snapshots WHERE competition_id = $competitionId
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
        IReadOnlyCollection<ChallengeSnapshot> snapshots) =>
        ReplaceSnapshots(
            "challenge_snapshots",
            competitionId,
            snapshots,
            static (connection, transaction, snapshot) =>
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO challenge_snapshots(
                        competition_id, challenge_id, title, published, content_hash)
                    VALUES($competitionId, $challengeId, $title, $published, $contentHash)
                    """;
                insert.Parameters.AddWithValue("$competitionId", snapshot.CompetitionId.ToString("D"));
                insert.Parameters.AddWithValue("$challengeId", snapshot.ChallengeId.ToString("D"));
                insert.Parameters.AddWithValue("$title", snapshot.Title);
                insert.Parameters.AddWithValue("$published", snapshot.Published ? 1 : 0);
                insert.Parameters.AddWithValue("$contentHash", snapshot.ContentHash);
                insert.ExecuteNonQuery();
            });

    public IReadOnlyDictionary<Guid, TeamSnapshot> GetTeamSnapshots(Guid competitionId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT competition_id, team_id, team_name, rank, score,
                   achievement_hash, achievements_json
            FROM team_snapshots WHERE competition_id = $competitionId
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
        IReadOnlyCollection<TeamSnapshot> snapshots) =>
        ReplaceSnapshots(
            "team_snapshots",
            competitionId,
            snapshots,
            static (connection, transaction, snapshot) =>
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
                insert.Parameters.AddWithValue("$competitionId", snapshot.CompetitionId.ToString("D"));
                insert.Parameters.AddWithValue("$teamId", snapshot.TeamId.ToString("D"));
                insert.Parameters.AddWithValue("$teamName", snapshot.TeamName);
                insert.Parameters.AddWithValue("$rank", snapshot.Rank is null ? DBNull.Value : snapshot.Rank.Value);
                insert.Parameters.AddWithValue("$score", snapshot.Score);
                insert.Parameters.AddWithValue("$achievementHash", snapshot.AchievementHash);
                insert.Parameters.AddWithValue("$achievementsJson", snapshot.AchievementsJson);
                insert.ExecuteNonQuery();
            });

    public bool EnqueueOutbound(
        string dedupeKey,
        string providerId,
        string groupId,
        string payload)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO outbound_messages(
                    id, dedupe_key, provider_id, group_id, payload, state, attempt_count,
                    next_attempt_at, last_error, created_at, updated_at)
                VALUES($id, $dedupeKey, $providerId, $groupId, $payload, $state, 0,
                       $nextAttemptAt, NULL, $createdAt, $updatedAt)
                """;
            var now = NowText();
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$dedupeKey", dedupeKey);
            command.Parameters.AddWithValue("$providerId", providerId);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$state", (int)OutboundMessageState.Pending);
            command.Parameters.AddWithValue("$nextAttemptAt", now);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$updatedAt", now);
            return command.ExecuteNonQuery() == 1;
        }
    }

    public OutboundMessage? ClaimDueOutbound(string providerId)
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
                    SELECT id, dedupe_key, provider_id, group_id, payload,
                           state, attempt_count, next_attempt_at
                    FROM outbound_messages
                    WHERE provider_id = $providerId AND state = $pending
                      AND next_attempt_at <= $now
                    ORDER BY next_attempt_at, created_at
                    LIMIT 1
                    """;
                select.Parameters.AddWithValue("$providerId", providerId);
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

    public void CompleteOutbound(Guid id) =>
        UpdateOutbound(id, OutboundMessageState.Completed, null, null);

    public void FailOutbound(Guid id, int attemptCount, string safeError)
    {
        var delay = OutboundRetrySchedule.AfterFailure(attemptCount);
        UpdateOutbound(
            id,
            delay is null ? OutboundMessageState.DeadLetter : OutboundMessageState.Pending,
            delay is null ? null : timeProvider.GetUtcNow() + delay,
            safeError.Length <= 300 ? safeError : safeError[..300]);
    }

    private void ReplaceSnapshots<T>(
        string table,
        Guid competitionId,
        IReadOnlyCollection<T> snapshots,
        Action<SqliteConnection, SqliteTransaction, T> insert)
    {
        lock (writeLock)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = $"DELETE FROM {table} WHERE competition_id = $competitionId";
                delete.Parameters.AddWithValue("$competitionId", competitionId.ToString("D"));
                delete.ExecuteNonQuery();
            }
            foreach (var snapshot in snapshots) insert(connection, transaction, snapshot);
            transaction.Commit();
        }
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

    private static void CancelPendingOutbound(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string providerId,
        string groupId,
        string reason,
        string updatedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbound_messages
            SET state = $deadLetter, last_error = $reason, updated_at = $updatedAt
            WHERE provider_id = $providerId AND group_id = $groupId
              AND state IN ($pending, $sending)
            """;
        command.Parameters.AddWithValue("$deadLetter", (int)OutboundMessageState.DeadLetter);
        command.Parameters.AddWithValue("$pending", (int)OutboundMessageState.Pending);
        command.Parameters.AddWithValue("$sending", (int)OutboundMessageState.Sending);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$updatedAt", updatedAt);
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        command.ExecuteNonQuery();
    }

    private static void ExecuteGroupDelete(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string providerId,
        string groupId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE provider_id = $providerId AND group_id = $groupId";
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$groupId", groupId);
        command.ExecuteNonQuery();
    }

    private static GroupAccess ReadGroupAccess(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : ParseTime(reader.GetString(2)),
            reader.GetBoolean(3),
            ParseTime(reader.GetString(4)));

    private static GroupSubscription ReadSubscription(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            ParseTime(reader.GetString(7)));

    private static OutboundMessage ReadOutbound(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            (OutboundMessageState)reader.GetInt32(5),
            reader.GetInt32(6),
            ParseTime(reader.GetString(7)));

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table)";
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static void CreateCurrentSchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, CurrentSchema, transaction);
        Execute(connection, $"PRAGMA user_version={CurrentSchemaVersion};", transaction);
        transaction.Commit();
    }

    private static void MigrateLegacySchema(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, LegacyMigration, transaction);
        Execute(connection, $"PRAGMA user_version={CurrentSchemaVersion};", transaction);
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString) { DefaultTimeout = CommandTimeoutSeconds };
        connection.Open();
        return connection;
    }

    private static void Execute(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.ExecuteNonQuery();
    }

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

    private const string SubscriptionSelect = """
        SELECT subscription.provider_id, subscription.group_id, subscription.competition_id,
               subscription.broadcast_enabled, subscription.scoreboard_enabled,
               subscription.blood_enabled, subscription.suspended_reason, subscription.updated_at
        FROM group_subscriptions AS subscription
        JOIN group_access AS access
          ON access.provider_id = subscription.provider_id
         AND access.group_id = subscription.group_id
         AND access.master_authorized_at IS NOT NULL
         AND access.enabled = 1
        """;

    private const string CurrentSchema = """
        CREATE TABLE group_access (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL,
            master_authorized_at TEXT NULL, enabled INTEGER NOT NULL, updated_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id));
        CREATE TABLE group_admins (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, user_id TEXT NOT NULL,
            granted_by TEXT NOT NULL, granted_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id, user_id));
        CREATE TABLE group_subscriptions (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, competition_id TEXT NOT NULL,
            broadcast_enabled INTEGER NOT NULL, scoreboard_enabled INTEGER NOT NULL,
            blood_enabled INTEGER NOT NULL, suspended_reason TEXT NULL, updated_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id));
        CREATE INDEX ix_group_subscriptions_competition ON group_subscriptions(competition_id);
        CREATE TABLE announcement_checkpoints (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, competition_id TEXT NOT NULL,
            published_at TEXT NOT NULL, announcement_id TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id, competition_id));
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
        CREATE TABLE inbound_messages (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, message_id TEXT NOT NULL,
            received_at TEXT NOT NULL, PRIMARY KEY(provider_id, group_id, message_id));
        CREATE TABLE outbound_messages (
            id TEXT PRIMARY KEY, dedupe_key TEXT NOT NULL UNIQUE, provider_id TEXT NOT NULL,
            group_id TEXT NOT NULL, payload TEXT NOT NULL, state INTEGER NOT NULL,
            attempt_count INTEGER NOT NULL, next_attempt_at TEXT NOT NULL,
            last_error TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE INDEX ix_outbound_messages_due
            ON outbound_messages(provider_id, state, next_attempt_at, created_at);
        """;

    private const string LegacyMigration = """
        ALTER TABLE group_subscriptions RENAME TO legacy_group_subscriptions;
        ALTER TABLE inbound_messages RENAME TO legacy_inbound_messages;
        ALTER TABLE outbound_messages RENAME TO legacy_outbound_messages;
        DROP INDEX IF EXISTS ix_group_subscriptions_competition;
        DROP INDEX IF EXISTS ix_outbound_messages_due;

        CREATE TABLE group_access (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL,
            master_authorized_at TEXT NULL, enabled INTEGER NOT NULL, updated_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id));
        CREATE TABLE group_admins (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, user_id TEXT NOT NULL,
            granted_by TEXT NOT NULL, granted_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id, user_id));
        CREATE TABLE group_subscriptions (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, competition_id TEXT NOT NULL,
            broadcast_enabled INTEGER NOT NULL, scoreboard_enabled INTEGER NOT NULL,
            blood_enabled INTEGER NOT NULL, suspended_reason TEXT NULL, updated_at TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id));
        CREATE INDEX ix_group_subscriptions_competition ON group_subscriptions(competition_id);
        CREATE TABLE announcement_checkpoints (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, competition_id TEXT NOT NULL,
            published_at TEXT NOT NULL, announcement_id TEXT NOT NULL,
            PRIMARY KEY(provider_id, group_id, competition_id));
        CREATE TABLE inbound_messages (
            provider_id TEXT NOT NULL, group_id TEXT NOT NULL, message_id TEXT NOT NULL,
            received_at TEXT NOT NULL, PRIMARY KEY(provider_id, group_id, message_id));
        CREATE TABLE outbound_messages (
            id TEXT PRIMARY KEY, dedupe_key TEXT NOT NULL UNIQUE, provider_id TEXT NOT NULL,
            group_id TEXT NOT NULL, payload TEXT NOT NULL, state INTEGER NOT NULL,
            attempt_count INTEGER NOT NULL, next_attempt_at TEXT NOT NULL,
            last_error TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE INDEX ix_outbound_messages_due
            ON outbound_messages(provider_id, state, next_attempt_at, created_at);

        INSERT INTO group_access(provider_id, group_id, master_authorized_at, enabled, updated_at)
        SELECT 'milky', CAST(group_id AS TEXT), NULL, 0, updated_at FROM legacy_group_subscriptions;
        INSERT INTO group_subscriptions(
            provider_id, group_id, competition_id, broadcast_enabled,
            scoreboard_enabled, blood_enabled, suspended_reason, updated_at)
        SELECT 'milky', CAST(group_id AS TEXT), competition_id, broadcast_enabled,
               scoreboard_enabled, blood_enabled, suspended_reason, updated_at
        FROM legacy_group_subscriptions;
        INSERT INTO inbound_messages(provider_id, group_id, message_id, received_at)
        SELECT 'milky', CAST(peer_id AS TEXT), CAST(message_sequence AS TEXT), received_at
        FROM legacy_inbound_messages;
        INSERT INTO outbound_messages(
            id, dedupe_key, provider_id, group_id, payload, state, attempt_count,
            next_attempt_at, last_error, created_at, updated_at)
        SELECT id, dedupe_key, 'milky', CAST(group_id AS TEXT), payload,
               CASE WHEN state IN (0, 1) THEN 3 ELSE state END,
               attempt_count, next_attempt_at,
               CASE WHEN state IN (0, 1) THEN 'schema_upgrade_requires_master_enable' ELSE last_error END,
               created_at, updated_at
        FROM legacy_outbound_messages;

        DROP TABLE legacy_group_subscriptions;
        DROP TABLE legacy_inbound_messages;
        DROP TABLE legacy_outbound_messages;
        """;
}

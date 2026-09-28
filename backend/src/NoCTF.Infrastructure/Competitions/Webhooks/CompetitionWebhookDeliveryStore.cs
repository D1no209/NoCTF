using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Observability;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

public sealed record CompetitionWebhookOptions(
    Uri PublicBaseUrl,
    int TimeoutSeconds,
    IReadOnlySet<string> PrivateNetworkAllowList,
    IReadOnlySet<string> InsecureHttpHostAllowList);

public sealed class CompetitionWebhookDeliveryStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    GetChallenge getChallenge,
    GetCompetitionTracks getTracks,
    ILeaderboardCache leaderboard,
    CompetitionWebhookOptions options,
    TimeProvider timeProvider,
    ILogger<CompetitionWebhookDeliveryStore> logger) : ICompetitionWebhookDeliveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly TimeSpan[] ProjectionRetryDelays =
    [
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)
    ];
    private static readonly TimeSpan[] HttpRetryDelays =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30), TimeSpan.FromHours(2),
        TimeSpan.FromHours(8), TimeSpan.FromHours(24)
    ];

    public async Task<CompetitionWebhookPendingSnapshot> ReadPendingSnapshotAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = db.CompetitionWebhookDeliveries.AsNoTracking()
            .Where(item => item.State == CompetitionWebhookDeliveryState.Pending
                || item.State == CompetitionWebhookDeliveryState.InFlight);
        var outbox = db.CompetitionWebhookOutboxEvents.AsNoTracking()
            .Where(item => item.DispatchCompletedAt == null);
        var pendingCount = await pending.LongCountAsync(cancellationToken);
        var undispatchedCount = await outbox.LongCountAsync(cancellationToken);
        var oldestDelivery = await pending
            .Select(item => (DateTimeOffset?)item.DomainEventCreatedAt)
            .MinAsync(cancellationToken);
        var oldestOutbox = await outbox
            .Select(item => (DateTimeOffset?)item.DomainEventCreatedAt)
            .MinAsync(cancellationToken);
        var oldest = new[] { oldestDelivery, oldestOutbox }
            .Where(item => item is not null)
            .Min();
        return new(pendingCount, undispatchedCount,
            oldest is { } occurredAt
                ? Math.Max(0, (long)(now - occurredAt).TotalSeconds)
                : 0);
    }

    public async Task<CompetitionWebhookDeliveryDiagnosticPage?> ListDiagnosticsAsync(
        Guid competitionId, int offset, int limit, bool descending,
        CancellationToken cancellationToken)
    {
        if (!await db.Competitions.AsNoTracking()
                .AnyAsync(item => item.Id == competitionId, cancellationToken))
            return null;
        var query = db.CompetitionWebhookDeliveries.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId);
        var total = await query.CountAsync(cancellationToken);
        var ordered = descending
            ? query.OrderByDescending(item => item.DomainEventCreatedAt)
                .ThenByDescending(item => item.EventId)
            : query.OrderBy(item => item.DomainEventCreatedAt)
                .ThenBy(item => item.EventId);
        var rows = await ordered.Skip(offset).Take(limit)
            .Join(db.CompetitionEvents.AsNoTracking(), delivery => delivery.EventId,
                competitionEvent => competitionEvent.Id,
                (delivery, competitionEvent) => new { delivery, competitionEvent.Kind })
            .Join(db.CompetitionWebhookOutboxEvents.AsNoTracking(),
                item => item.delivery.EventId, outbox => outbox.EventId,
                (item, outbox) => new
                {
                    item.delivery,
                    item.Kind,
                    outbox.Sequence,
                    outbox.CompetitionRevision
                })
            .ToArrayAsync(cancellationToken);
        return new(rows.Select(item =>
        {
            var row = item.delivery;
            return new CompetitionWebhookDeliveryDiagnostic(
                row.EventId, row.TargetId, item.Kind, row.State,
                row.PayloadState, item.Sequence, item.CompetitionRevision,
                row.DomainEventCreatedAt, row.OutboxPersistedAt,
                row.WorkerDequeuedAt, row.PublicProjectionReadyAt,
                row.CapturedAt, row.FirstHttpAttemptStartedAt,
                row.LastHttpAttemptStartedAt, row.LastHttpAttemptCompletedAt,
                row.LastHttpStatusCode, row.ProjectionRetryCount,
                row.HttpRetryCount,
                row.State is CompetitionWebhookDeliveryState.Pending
                    or CompetitionWebhookDeliveryState.InFlight
                    ? row.NextRetryAt : null,
                row.DeadLetterReason);
        }).ToArray(), total);
    }

    public async Task<IReadOnlyList<CompetitionWebhookOutboxWakeup>> ClaimPendingOutboxAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await db.CompetitionWebhookOutboxEvents.AsNoTracking()
            .Where(item => item.DispatchCompletedAt == null && item.NextDispatchAt <= now)
            .OrderBy(item => item.Sequence)
            .Take(limit)
            .Select(item => new CompetitionWebhookOutboxWakeup(
                item.CompetitionId, item.EventId))
            .ToArrayAsync(cancellationToken);
        var claimed = new List<CompetitionWebhookOutboxWakeup>(candidates.Length);
        var retryAt = now.AddSeconds(3);
        foreach (var candidate in candidates)
        {
            var updated = await db.CompetitionWebhookOutboxEvents
                .Where(item => item.EventId == candidate.EventId
                    && item.DispatchCompletedAt == null
                    && item.NextDispatchAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.NextDispatchAt, retryAt)
                    .SetProperty(item => item.WorkerDequeuedAt, now), cancellationToken);
            if (updated == 1)
                claimed.Add(candidate);
        }
        return claimed;
    }

    public async Task MarkDispatchCompletedAsync(Guid eventId, DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        _ = await db.CompetitionWebhookOutboxEvents
            .Where(item => item.EventId == eventId && item.DispatchCompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.DispatchCompletedAt, completedAt), cancellationToken);
    }

    public async Task<IReadOnlyList<DeliverCompetitionWebhook>> ClaimDueDeliveriesAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var due = await db.CompetitionWebhookDeliveries.AsNoTracking()
            .Where(item => item.State == CompetitionWebhookDeliveryState.Pending
                    && item.NextRetryAt <= now
                    && (item.EnqueueLeaseUntil == null || item.EnqueueLeaseUntil <= now)
                || item.State == CompetitionWebhookDeliveryState.InFlight
                    && item.LeaseExpiresAt <= now)
            .OrderBy(item => item.DomainEventCreatedAt).ThenBy(item => item.EventId)
            .Take(limit)
            .Select(item => new DeliverCompetitionWebhook(
                item.CompetitionId, item.EventId, item.TargetId,
                item.DomainEventCreatedAt))
            .ToArrayAsync(cancellationToken);
        var claimed = new List<DeliverCompetitionWebhook>(due.Length);
        foreach (var item in due)
        {
            var updated = await db.CompetitionWebhookDeliveries
                .Where(row => row.EventId == item.EventId && row.TargetId == item.TargetId
                    && (row.State == CompetitionWebhookDeliveryState.Pending
                            && row.NextRetryAt <= now
                            && (row.EnqueueLeaseUntil == null || row.EnqueueLeaseUntil <= now)
                        || row.State == CompetitionWebhookDeliveryState.InFlight
                            && row.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.State, CompetitionWebhookDeliveryState.Pending)
                    .SetProperty(row => row.EnqueueLeaseUntil, now.AddSeconds(5))
                    .SetProperty(row => row.LeaseExpiresAt, (DateTimeOffset?)null),
                    cancellationToken);
            if (updated == 1)
                claimed.Add(item);
        }
        return claimed;
    }

    public async Task RecordProjectionWaitAsync(DeliverCompetitionWebhook delivery,
        CompetitionWebhookProjectionFailure failure, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await FindDeliveryAsync(delivery, cancellationToken);
        if (row is null)
            throw new CompetitionWebhookProjectionNotReadyException(failure);
        if (failure == CompetitionWebhookProjectionFailure.BlackoutSuppressed)
        {
            row.State = CompetitionWebhookDeliveryState.Suppressed;
            row.LeaseExpiresAt = null;
            row.EnqueueLeaseUntil = null;
            row.CompletedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        row.ProjectionRetryCount++;
        row.PayloadState = failure == CompetitionWebhookProjectionFailure.MissingEventIdentity
            ? CompetitionWebhookPayloadState.Invalid
            : CompetitionWebhookPayloadState.ProjectionNotReady;
        row.LeaseExpiresAt = null;
        row.EnqueueLeaseUntil = null;
        if (row.ProjectionRetryCount > ProjectionRetryDelays.Length)
        {
            row.State = CompetitionWebhookDeliveryState.DeadLetter;
            row.CompletedAt = now;
            row.DeadLetterReason = failure == CompetitionWebhookProjectionFailure.MissingEventIdentity
                ? CompetitionWebhookDeadLetterReason.InvalidPayload
                : CompetitionWebhookDeadLetterReason.ProjectionUnavailable;
            NoCtfTelemetry.RecordWebhookDeadLetter(row.DeadLetterReason.Value.ToString());
        }
        else
        {
            row.State = CompetitionWebhookDeliveryState.Pending;
            row.NextRetryAt = now.Add(ProjectionRetryDelays[row.ProjectionRetryCount - 1]);
            NoCtfTelemetry.RecordWebhookRetry("projection");
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordHttpStartedAsync(DeliverCompetitionWebhook delivery,
        DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var row = await FindDeliveryAsync(delivery, cancellationToken);
        if (row is null) return;
        if (row.FirstHttpAttemptStartedAt is null)
            NoCtfTelemetry.RecordWebhookQueueAge(
                (startedAt - row.DomainEventCreatedAt).TotalSeconds);
        row.FirstHttpAttemptStartedAt ??= startedAt;
        row.LastHttpAttemptStartedAt = startedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordHttpCompletedAsync(DeliverCompetitionWebhook delivery,
        CompetitionWebhookSendOutcome outcome, DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var row = await FindDeliveryAsync(delivery, cancellationToken);
        if (row is null) return;
        row.LastHttpStatusCode = outcome.HttpStatusCode;
        row.LastHttpAttemptCompletedAt = completedAt;
        if (row.LastHttpAttemptStartedAt is { } startedAt)
            NoCtfTelemetry.RecordWebhookHttpAttempt(
                (completedAt - startedAt).TotalSeconds,
                outcome.Result == CompetitionWebhookSendResult.Delivered
                    ? "delivered" : "receiver_gone");
        row.CompletedAt = completedAt;
        row.LeaseExpiresAt = null;
        row.EnqueueLeaseUntil = null;
        row.State = outcome.Result == CompetitionWebhookSendResult.Delivered
            ? CompetitionWebhookDeliveryState.Delivered
            : CompetitionWebhookDeliveryState.Suppressed;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordHttpFailureAsync(DeliverCompetitionWebhook delivery,
        int? statusCode, DateTimeOffset? retryAfter, bool permanent,
        DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        var row = await FindDeliveryAsync(delivery, cancellationToken);
        if (row is null)
        {
            if (permanent)
                throw new CompetitionWebhookPermanentException(
                    "Legacy webhook delivery failed permanently.", statusCode);
            throw new CompetitionWebhookTransientException(
                "Legacy webhook delivery failed transiently.",
                httpStatusCode: statusCode, retryAfter: retryAfter);
        }
        row.HttpRetryCount++;
        row.LastHttpStatusCode = statusCode;
        row.LastHttpAttemptCompletedAt = completedAt;
        if (row.LastHttpAttemptStartedAt is { } startedAt)
            NoCtfTelemetry.RecordWebhookHttpAttempt(
                (completedAt - startedAt).TotalSeconds, "failed");
        row.LeaseExpiresAt = null;
        row.EnqueueLeaseUntil = null;
        if (permanent || row.HttpRetryCount > HttpRetryDelays.Length)
        {
            row.State = CompetitionWebhookDeliveryState.DeadLetter;
            row.CompletedAt = completedAt;
            row.DeadLetterReason = permanent
                ? CompetitionWebhookDeadLetterReason.PermanentHttpFailure
                : CompetitionWebhookDeadLetterReason.HttpRetryLimitExceeded;
            NoCtfTelemetry.RecordWebhookDeadLetter(row.DeadLetterReason.Value.ToString());
        }
        else
        {
            row.State = CompetitionWebhookDeliveryState.Pending;
            var backoff = completedAt.Add(HttpRetryDelays[row.HttpRetryCount - 1]);
            row.NextRetryAt = retryAfter is { } requested && requested > backoff
                ? requested : backoff;
            NoCtfTelemetry.RecordWebhookRetry("http");
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CompetitionWebhookDeliveryRecord?> FindDeliveryAsync(
        DeliverCompetitionWebhook delivery, CancellationToken cancellationToken)
    {
        var tracked = db.CompetitionWebhookDeliveries.Local.FirstOrDefault(item =>
            item.EventId == delivery.EventId && item.TargetId == delivery.TargetId);
        if (tracked is not null)
        {
            await db.Entry(tracked).ReloadAsync(cancellationToken);
            return db.Entry(tracked).State == EntityState.Detached ? null : tracked;
        }
        return await db.CompetitionWebhookDeliveries.SingleOrDefaultAsync(item =>
            item.EventId == delivery.EventId && item.TargetId == delivery.TargetId,
            cancellationToken);
    }

    public async Task<CompetitionWebhookDispatchBatch> PrepareBatchAsync(
        DispatchCompetitionWebhooks command,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var source = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.Id == command.EventId
                && item.CompetitionId == command.CompetitionId)
            .Select(item => new
            {
                item.Visibility, item.Kind, item.OccurredAt,
                item.LeaderboardVisibility
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (source is null
            || source.Visibility != CompetitionEventVisibility.Public
            || CompetitionWebhookEventTypes.From(source.Kind) is null)
            return new([]);

        var configuration = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == command.CompetitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.WebhookConfiguration,
                item.FrozenStartAt,
                item.HiddenStartAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (configuration is null)
            return new([]);
        if (CompetitionWebhookEventTypes.IsBloodAward(source.Kind)
            && (source.LeaderboardVisibility == CompetitionLeaderboardVisibility.Blackout
                || !CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                    configuration.FrozenStartAt,
                    configuration.HiddenStartAt,
                    timeProvider.GetUtcNow())
                || !CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                    configuration.FrozenStartAt,
                    configuration.HiddenStartAt,
                    source.OccurredAt)))
            return new([]);

        var page = configuration.WebhookConfiguration.Targets
            .Where(item => command.AfterTargetId is null
                || item.Id.CompareTo(command.AfterTargetId.Value) > 0)
            .OrderBy(item => item.Id)
            .Take(batchSize + 1)
            .ToArray();
        var deliveries = page.Take(batchSize)
            .Where(item => item.Enabled
                && item.EnabledAt is DateTimeOffset enabledAt
                && source.OccurredAt >= enabledAt)
            .Select(item => new DeliverCompetitionWebhook(
                command.CompetitionId,
                command.EventId,
                item.Id,
                source.OccurredAt))
            .ToArray();
        var outbox = await db.CompetitionWebhookOutboxEvents.AsNoTracking()
            .Where(item => item.EventId == command.EventId)
            .Select(item => new { item.OutboxPersistedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (outbox is not null && deliveries.Length > 0)
        {
            var targetIds = deliveries.Select(item => item.TargetId).ToArray();
            var existing = (await db.CompetitionWebhookDeliveries.AsNoTracking()
                .Where(item => item.EventId == command.EventId
                    && targetIds.Contains(item.TargetId))
                .Select(item => item.TargetId)
                .ToArrayAsync(cancellationToken)).ToHashSet();
            var createdAt = timeProvider.GetUtcNow();
            foreach (var delivery in deliveries.Where(item => !existing.Contains(item.TargetId)))
                db.CompetitionWebhookDeliveries.Add(new CompetitionWebhookDeliveryRecord
                {
                    EventId = delivery.EventId,
                    TargetId = delivery.TargetId,
                    CompetitionId = delivery.CompetitionId,
                    State = CompetitionWebhookDeliveryState.Pending,
                    PayloadState = CompetitionWebhookPayloadState.Unknown,
                    DomainEventCreatedAt = source.OccurredAt,
                    OutboxPersistedAt = outbox.OutboxPersistedAt,
                    CreatedAt = createdAt,
                    NextRetryAt = createdAt,
                    EnqueueLeaseUntil = createdAt.AddMilliseconds(500)
                });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The JetStream wakeup and the PostgreSQL recovery scanner may race.
                // Treat an already materialized complete batch as idempotent.
                db.ChangeTracker.Clear();
                var persisted = await db.CompetitionWebhookDeliveries.AsNoTracking()
                    .Where(item => item.EventId == command.EventId
                        && targetIds.Contains(item.TargetId))
                    .Select(item => item.TargetId)
                    .Distinct()
                    .CountAsync(cancellationToken);
                if (persisted != targetIds.Length)
                    throw;
                NoCtfTelemetry.RecordWebhookMaterializationRace();
                logger.LogInformation(
                    "Webhook delivery materialization race recovered for event {EventId}.",
                    command.EventId);
            }
        }
        return new(
            deliveries,
            page.Length > batchSize ? page[batchSize - 1].Id : null);
    }

    public async Task<CompetitionWebhookDelivery> PrepareDeliveryAsync(
        DeliverCompetitionWebhook command,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == command.CompetitionId, cancellationToken);
        if (competition is null || competition.DeletedAt is not null)
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Missing(command);
        }
        var target = competition.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == command.TargetId);
        if (target is null)
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Missing(command);
        }
        if (!target.Enabled
            || target.EnabledAt is not DateTimeOffset enabledAt
            || command.OccurredAt < enabledAt)
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Suppressed(command);
        }

        var competitionEvent = await db.CompetitionEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.EventId
                && item.CompetitionId == command.CompetitionId, cancellationToken);
        if (competitionEvent is null)
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Missing(command);
        }
        var eventType = CompetitionWebhookEventTypes.From(competitionEvent.Kind);
        if (competitionEvent.Visibility != CompetitionEventVisibility.Public
            || eventType is null)
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Suppressed(command);
        }
        if (CompetitionWebhookEventTypes.IsBloodAward(competitionEvent.Kind)
            && (competitionEvent.LeaderboardVisibility == CompetitionLeaderboardVisibility.Blackout
                || !CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                    competition.FrozenStartAt, competition.HiddenStartAt,
                    timeProvider.GetUtcNow())
                || !CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                    competition.FrozenStartAt, competition.HiddenStartAt,
                    competitionEvent.OccurredAt)))
        {
            await MarkSuppressedAsync(command, cancellationToken);
            return Suppressed(command);
        }

        if (!await TryClaimDeliveryAsync(command, cancellationToken))
            return Suppressed(command);

        var body = await BuildBodyAsync(
            competition,
            competitionEvent,
            eventType,
            cancellationToken);
        await RecordPayloadReadyAsync(command, body, cancellationToken);
        var previousSecret = target.PreviousSecretCiphertext is { Length: > 0 }
            && target.PreviousSecretValidUntil > timeProvider.GetUtcNow()
                ? secrets.Unprotect(
                    target.PreviousSecretCiphertext,
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competition.Id,
                    target.Id)
                : null;
        return new(
            CompetitionWebhookDeliveryReadState.Ready,
            command.CompetitionId,
            command.EventId,
            command.TargetId,
            new Uri(target.EndpointUrl),
            body,
            secrets.Unprotect(
                target.CurrentSecretCiphertext,
                PlatformSecretPurpose.CompetitionWebhookSecret,
                competition.Id,
                target.Id),
            previousSecret);
    }

    private async Task<bool> TryClaimDeliveryAsync(
        DeliverCompetitionWebhook command, CancellationToken cancellationToken)
    {
        var exists = await db.CompetitionWebhookDeliveries.AsNoTracking()
            .AnyAsync(item => item.EventId == command.EventId
                && item.TargetId == command.TargetId, cancellationToken);
        if (!exists) return true; // Legacy events created before the transactional outbox.
        var now = timeProvider.GetUtcNow();
        var claimed = await db.CompetitionWebhookDeliveries
            .Where(item => item.EventId == command.EventId
                && item.TargetId == command.TargetId
                && (item.State == CompetitionWebhookDeliveryState.Pending
                        && item.NextRetryAt <= now
                    || item.State == CompetitionWebhookDeliveryState.InFlight
                        && item.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, CompetitionWebhookDeliveryState.InFlight)
                .SetProperty(item => item.LeaseExpiresAt, now.AddMinutes(1))
                .SetProperty(item => item.WorkerDequeuedAt, now), cancellationToken);
        return claimed == 1;
    }

    private async Task MarkSuppressedAsync(
        DeliverCompetitionWebhook command, CancellationToken cancellationToken)
    {
        _ = await db.CompetitionWebhookDeliveries
            .Where(item => item.EventId == command.EventId
                && item.TargetId == command.TargetId
                && item.State != CompetitionWebhookDeliveryState.Delivered)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.State, CompetitionWebhookDeliveryState.Suppressed)
                .SetProperty(item => item.CompletedAt, timeProvider.GetUtcNow()),
                cancellationToken);
    }

    private async Task RecordPayloadReadyAsync(
        DeliverCompetitionWebhook command, byte[] body,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(body);
        var capturedAt = document.RootElement.GetProperty("data")
            .GetProperty("capturedAt").GetDateTimeOffset();
        var readyAt = timeProvider.GetUtcNow();
        var waitStarted = await db.CompetitionWebhookDeliveries.AsNoTracking()
            .Where(item => item.EventId == command.EventId
                && item.TargetId == command.TargetId)
            .Select(item => item.WorkerDequeuedAt)
            .SingleOrDefaultAsync(cancellationToken);
        _ = await db.CompetitionWebhookDeliveries
            .Where(item => item.EventId == command.EventId
                && item.TargetId == command.TargetId
                && item.State == CompetitionWebhookDeliveryState.InFlight)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PayloadState, CompetitionWebhookPayloadState.Complete)
                .SetProperty(item => item.PublicProjectionReadyAt, readyAt)
                .SetProperty(item => item.CapturedAt, capturedAt), cancellationToken);
        if (waitStarted is { } startedAt)
            NoCtfTelemetry.RecordWebhookProjectionWait(
                (readyAt - startedAt).TotalSeconds);
    }

    public async Task<CompetitionWebhookDelivery> PrepareTestDeliveryAsync(
        TestCompetitionWebhook command,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == command.CompetitionId, cancellationToken);
        var target = competition?.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == command.TargetId);
        if (competition is null || target is null)
        {
            return new(
                CompetitionWebhookDeliveryReadState.Missing,
                command.CompetitionId,
                command.DeliveryId,
                command.TargetId);
        }
        var now = timeProvider.GetUtcNow();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            specversion = "1.0",
            id = command.DeliveryId,
            source = new Uri(options.PublicBaseUrl,
                $"api/v1/competitions/{competition.Id}").AbsoluteUri,
            type = "com.noctf.webhook.test.v1",
            subject = $"competitions/{competition.Id}",
            time = command.RequestedAt,
            datacontenttype = "application/json",
            dataschema = new Uri(options.PublicBaseUrl,
                "schemas/webhooks/competition-events-v1.schema.json").AbsoluteUri,
            data = new
            {
                capturedAt = now,
                @event = new { competitionId = competition.Id },
                resources = new { competition = BuildCompetitionResource(competition) }
            }
        }, JsonOptions);
        var previousSecret = target.PreviousSecretCiphertext is { Length: > 0 }
            && target.PreviousSecretValidUntil > now
                ? secrets.Unprotect(
                    target.PreviousSecretCiphertext,
                    PlatformSecretPurpose.CompetitionWebhookSecret,
                    competition.Id,
                    target.Id)
                : null;
        return new(
            CompetitionWebhookDeliveryReadState.Ready,
            competition.Id,
            command.DeliveryId,
            target.Id,
            new Uri(target.EndpointUrl),
            body,
            secrets.Unprotect(
                target.CurrentSecretCiphertext,
                PlatformSecretPurpose.CompetitionWebhookSecret,
                competition.Id,
                target.Id),
            previousSecret);
    }

    public async Task DisableGoneAsync(
        Guid competitionId,
        Guid targetId,
        Uri expectedEndpoint,
        DateTimeOffset disabledAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var competition = await db.Competitions.AsSplitQuery().SingleOrDefaultAsync(
            item => item.Id == competitionId,
            cancellationToken);
        var target = competition?.WebhookConfiguration.Targets
            .SingleOrDefault(item => item.Id == targetId);
        if (target is null
            || !target.Enabled
            || !string.Equals(target.EndpointUrl, expectedEndpoint.AbsoluteUri, StringComparison.Ordinal))
            return;
        target.Enabled = false;
        target.EnabledAt = null;
        target.DisabledReason = CompetitionWebhookDisabledReason.ReceiverGone;
        target.UpdatedAt = disabledAt;
        competition!.UpdatedAt = disabledAt;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<byte[]> BuildBodyAsync(
        Competition competition,
        CompetitionEvent competitionEvent,
        string eventType,
        CancellationToken cancellationToken)
    {
        var challengeId = competitionEvent.CompetitionChallengeId;
        var competitionResource = BuildCompetitionResource(competition,
            CompetitionWebhookEventTypes.IsBloodAward(competitionEvent.Kind)
                ? competitionEvent.LeaderboardVisibility : null);
        var outbox = await db.CompetitionWebhookOutboxEvents.AsNoTracking()
            .Where(item => item.EventId == competitionEvent.Id)
            .Select(item => new { item.Sequence, item.CompetitionRevision })
            .SingleOrDefaultAsync(cancellationToken);
        var leaderboardData = CompetitionWebhookEventTypes.RequiresLeaderboard(
                competitionEvent.Kind)
            ? await BuildLeaderboardResourceAsync(competition,
                CompetitionWebhookEventTypes.IsBloodAward(competitionEvent.Kind)
                    ? competitionEvent.LeaderboardVisibility : null,
                competitionEvent.FrozenStartAt ?? competition.FrozenStartAt,
                cancellationToken)
            : null;
        if (leaderboardData is { Scope: LeaderboardDataScope.Live }
            && outbox is not null
            && (leaderboardData.Projection is null
                || leaderboardData.SourceEventSequenceThrough < outbox.Sequence))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        if (leaderboardData is { Scope: LeaderboardDataScope.Frozen, Projection: null }
            && outbox is not null)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        JsonElement? challengeResource = CompetitionWebhookEventTypes.IsBloodAward(
                competitionEvent.Kind)
            ? BuildBloodChallengeResource(competition, competitionEvent, leaderboardData,
                outbox?.Sequence ?? 0,
                competitionEvent.FrozenStartAt ?? competition.FrozenStartAt)
            : challengeId is Guid id
                ? await BuildChallengeResourceAsync(competition, id, cancellationToken)
                : null;
        JsonElement? announcementResource = competitionEvent.Kind == CompetitionEventKind.AnnouncementPublished
            ? await BuildAnnouncementResourceAsync(competitionEvent.QuestionId, cancellationToken)
            : null;
        if (competitionEvent.Kind == CompetitionEventKind.AnnouncementPublished
            && announcementResource is null)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicAnnouncement);
        if (competitionEvent.Kind is CompetitionEventKind.ChallengePublished
                or CompetitionEventKind.ChallengeDescriptionUpdated
                or CompetitionEventKind.HintPublished
            && challengeResource is null)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicChallenge);
        var capturedAt = timeProvider.GetUtcNow();
        var source = new Uri(
            options.PublicBaseUrl,
            $"api/v1/competitions/{competition.Id}").AbsoluteUri;
        var schema = new Uri(
            options.PublicBaseUrl,
            "schemas/webhooks/competition-events-v1.schema.json").AbsoluteUri;
        var payload = new
        {
            specversion = "1.0",
            id = competitionEvent.Id,
            source,
            type = eventType,
            subject = $"competitions/{competition.Id}",
            time = competitionEvent.OccurredAt,
            datacontenttype = "application/json",
            dataschema = schema,
            data = new
            {
                capturedAt,
                eventSequence = outbox?.Sequence,
                requiredProjectionVersion = leaderboardData?.Scope == LeaderboardDataScope.Frozen
                    ? leaderboardData.SourceEventSequenceThrough
                    : outbox?.Sequence,
                publicProjectionVersion = leaderboardData?.SourceEventSequenceThrough
                    ?? outbox?.Sequence,
                competitionRevision = outbox?.CompetitionRevision,
                @event = new
                {
                    competitionId = competition.Id,
                    competitionChallengeId = challengeId,
                    hintId = competitionEvent.HintId,
                    notificationId = competitionEvent.QuestionId,
                    teamId = competitionEvent.TeamId,
                    gameplayFactId = competitionEvent.GameplayFactId,
                    from = competitionEvent.PreviousCompetitionStatus,
                    to = competitionEvent.CompetitionStatus,
                    award = CompetitionWebhookEventTypes.Award(competitionEvent.Kind),
                    outcome = competitionEvent.GameplayFactResult
                },
                resources = new
                {
                    competition = competitionResource,
                    challenge = challengeResource,
                    announcement = announcementResource,
                    leaderboard = leaderboardData?.Resource
                }
            }
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (CompetitionWebhookEventTypes.IsBloodAward(competitionEvent.Kind))
            BloodWebhookContract.Validate(body, competitionEvent, leaderboardData!.Scope);
        return body;
    }

    private static JsonElement BuildBloodChallengeResource(
        Competition competition,
        CompetitionEvent competitionEvent,
        PreparedLeaderboardResource? leaderboardData,
        long requiredProjectionVersion,
        DateTimeOffset? frozenAt)
    {
        if (competitionEvent.TeamId is not Guid { } teamId || teamId == Guid.Empty
            || competitionEvent.CompetitionChallengeId is not Guid { } challengeId
            || challengeId == Guid.Empty
            || CompetitionWebhookEventTypes.Award(competitionEvent.Kind) is null)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingEventIdentity);
        if (leaderboardData is null)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);

        if (leaderboardData.Scope == LeaderboardDataScope.Hidden)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.BlackoutSuppressed);

        var projection = leaderboardData.Projection
            ?? throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        if (leaderboardData.Scope == LeaderboardDataScope.Live
            && leaderboardData.SourceEventSequenceThrough < requiredProjectionVersion)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        if (leaderboardData.Scope == LeaderboardDataScope.Frozen
            && (frozenAt is null || projection.Snapshot.DataAsOf != frozenAt))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        var challenge = projection.ChallengeCatalog.Challenges.SingleOrDefault(
            item => item.CompetitionChallengeId == challengeId && item.IsPublished);
        if (challenge is null || string.IsNullOrWhiteSpace(challenge.Title))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicChallenge);
        var team = projection.Snapshot.Teams.SingleOrDefault(item => item.TeamId == teamId);
        if (team is null || string.IsNullOrWhiteSpace(team.TeamName))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicTeam);

        // The public challenge and team names come from the same published projection.
        // In Frozen scope this is the frozen projection, never the live challenge reader.
        return JsonSerializer.SerializeToElement(new
        {
            Id = challenge.CompetitionChallengeId,
            CompetitionId = competition.Id,
            challenge.Title,
            challenge.Direction,
            challenge.Order,
            challenge.IsPublished,
            DataScope = leaderboardData.Scope,
            LeaderboardVisibility = leaderboardData.Visibility,
            ProjectionVersion = leaderboardData.SourceEventSequenceThrough
                .ToString(CultureInfo.InvariantCulture)
        }, JsonOptions);
    }

    private JsonElement BuildCompetitionResource(
        Competition value,
        CompetitionLeaderboardVisibility? eventVisibility = null)
    {
        var now = timeProvider.GetUtcNow();
        var visibility = eventVisibility ?? CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            value.FrozenStartAt,
            value.HiddenStartAt,
            now);
        var posterUrl = value.PosterFileId is Guid fileId
            ? $"/api/v1/competitions/{value.Id}/poster?revision={fileId:N}"
            : null;
        return JsonSerializer.SerializeToElement(new
        {
            value.Id,
            value.Title,
            value.Description,
            PosterUrl = posterUrl,
            value.Mode,
            StartTime = value.StartAt,
            EndTime = value.EndAt,
            value.Status,
            value.TeamRegistrationAutoApprove,
            value.AllowTeamRegistrationWhileRunning,
            value.MaxTeamMembers,
            value.MaxConcurrentRuntimeInstancesPerTeam,
            value.OwnerId,
            LeaderboardVisibility = visibility,
            value.DeletedAt,
            AdministrationRole = (string?)null,
            value.MaxActiveQuestionsPerTeam,
            value.MaxParticipantMessagesBeforeHandlerReply,
            value.AllowChallengeOwnersToHandleQuestions,
            value.PracticeModeEnabled,
            value.TracksEnabled,
            value.AccessMode,
            value.WriteUpSubmissionRequired,
            value.WriteUpSubmissionDeadlineHours,
            WriteUpSubmissionDeadlineAt = CompetitionWriteUpPolicy.DeadlineAt(
                value.EndAt,
                value.WriteUpSubmissionDeadlineHours)
        }, JsonOptions);
    }

    private async Task<JsonElement?> BuildChallengeResourceAsync(
        Competition competition,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var view = await getChallenge.ExecuteAsync(
            competition.Id,
            competitionChallengeId,
            includeUnpublished: false,
            includeDeleted: false,
            cancellationToken);
        if (view is null)
            return null;
        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            timeProvider.GetUtcNow());
        var dataScope = visibility switch
        {
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            _ => LeaderboardDataScope.Live
        };
        var hints = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.Id == competitionChallengeId)
            .SelectMany(item => item.Hints)
            .Where(item => item.PublishedAt != null && item.HiddenAt == null)
            .OrderBy(item => item.PublishedAt)
            .Select(item => new
            {
                item.Id,
                item.Cost,
                PublishedAt = item.PublishedAt!.Value,
                Content = (string?)null,
                IsUnlocked = false,
                CanUnlock = false
            })
            .ToArrayAsync(cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.CustomTitle,
            view.Description,
            view.Direction,
            view.Order,
            view.IsPublished,
            view.DeletedAt,
            view.HasRuntime,
            view.CreatedAt,
            view.UpdatedAt,
            ControlFlag = (string?)null,
            Urls = (IReadOnlyList<string>?)null,
            LeaderboardVisibility = visibility,
            DataScope = dataScope,
            MaximumFlagAttempts = (int?)null,
            AcceptedFlagAttempts = (int?)null,
            RemainingFlagAttempts = (int?)null,
            SolvedByMyTeam = false,
            view.UsesDynamicFlag,
            Hints = hints,
            view.InteractionKind,
            PatchVerificationAvailable = false,
            MaximumPatchAttempts = (int?)null,
            AcceptedPatchAttempts = (int?)null,
            RemainingPatchAttempts = (int?)null,
            PatchVerificationState = (string?)null,
            PatchVerificationResult = (string?)null,
            PatchVerificationFailureCode = (string?)null,
            PatchVerificationRuntimeInstanceId = (Guid?)null,
            PatchVerificationRuntimeState = (string?)null
        }, JsonOptions);
    }

    private async Task<JsonElement?> BuildAnnouncementResourceAsync(
        Guid? notificationId,
        CancellationToken cancellationToken)
    {
        if (notificationId is not Guid id)
            return null;
        var row = await db.Notifications.AsNoTracking()
            .Where(item => item.Id == id
                && item.Kind == NotificationKind.CompetitionAnnouncement
                && item.TargetType == NotificationTargetType.CompetitionParticipants)
            .Select(item => new { item.Id, item.Title, item.Body, item.SentAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;
        return JsonSerializer.SerializeToElement(new
        {
            row.Id,
            row.Title,
            row.Body,
            PublishedAt = row.SentAt
        }, JsonOptions);
    }

    private async Task<PreparedLeaderboardResource> BuildLeaderboardResourceAsync(
        Competition competition,
        CompetitionLeaderboardVisibility? eventVisibility,
        DateTimeOffset? frozenAt,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var visibility = eventVisibility ?? CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            now);
        var dataScope = visibility switch
        {
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            _ => LeaderboardDataScope.Live
        };
        ScoreboardProjection? projection = null;
        long sourceEventSequenceThrough = 0;
        ScoreboardSnapshot snapshot;
        if (dataScope == LeaderboardDataScope.Hidden)
        {
            snapshot = new(competition.Id, 0, 0, now, null, [], []);
        }
        else
        {
            var published = dataScope == LeaderboardDataScope.Frozen
                ? frozenAt is { } boundary
                    ? await leaderboard.GetFrozenWebhookScoreboardAsync(
                        competition.Id, boundary, cancellationToken)
                    : null
                : await leaderboard.GetWebhookScoreboardAsync(
                    competition.Id, frozen: false, cancellationToken);
            projection = published?.Projection;
            sourceEventSequenceThrough = published?.SourceEventSequenceThrough ?? 0;
            if (projection is not null)
            {
                projection = ScoreboardAudienceProjection.ForPublishedChallenges(projection);
                var tracks = await getTracks.ExecuteAsync(
                    competition.Id,
                    viewerUserId: null,
                    includeInternal: false,
                    includeInvitationCodes: false,
                    cancellationToken);
                if (tracks is not null)
                {
                    projection = ScoreboardAudienceProjection.FilterTracks(
                        projection,
                        tracks,
                        canViewInternalTracks: false);
                }
            }
            snapshot = projection?.Snapshot
                ?? new ScoreboardSnapshot(competition.Id, 0, 0, now, null, [], []);
            snapshot = snapshot with
            {
                Visibility = visibility,
                DataScope = dataScope,
                DataAsOf = dataScope == LeaderboardDataScope.Frozen
                    ? snapshot.DataAsOf
                    : snapshot.GeneratedAt
            };
        }
        return new(SerializeScoreboard(snapshot with
        {
            Visibility = visibility,
            DataScope = dataScope
        }, sourceEventSequenceThrough), dataScope, visibility, projection,
            sourceEventSequenceThrough);
    }

    private sealed record PreparedLeaderboardResource(
        JsonElement Resource,
        LeaderboardDataScope Scope,
        CompetitionLeaderboardVisibility Visibility,
        ScoreboardProjection? Projection,
        long SourceEventSequenceThrough);

    private static JsonElement SerializeScoreboard(
        ScoreboardSnapshot snapshot, long sourceEventSequenceThrough) =>
        JsonSerializer.SerializeToElement(new
        {
            snapshot.CompetitionId,
            Version = snapshot.Version.ToString(CultureInfo.InvariantCulture),
            ProjectionVersion = sourceEventSequenceThrough.ToString(CultureInfo.InvariantCulture),
            SchemaRevision = snapshot.SchemaRevision.ToString(CultureInfo.InvariantCulture),
            snapshot.GeneratedAt,
            snapshot.CurrentRoundId,
            snapshot.Actors,
            snapshot.Teams,
            snapshot.Tracks,
            snapshot.TracksEnabled,
            snapshot.CurrentChallengeScores,
            snapshot.Visibility,
            snapshot.DataScope,
            snapshot.DataAsOf
        }, JsonOptions);

    private static CompetitionWebhookDelivery Missing(DeliverCompetitionWebhook command) => new(
        CompetitionWebhookDeliveryReadState.Missing,
        command.CompetitionId,
        command.EventId,
        command.TargetId);

    private static CompetitionWebhookDelivery Suppressed(DeliverCompetitionWebhook command) => new(
        CompetitionWebhookDeliveryReadState.Suppressed,
        command.CompetitionId,
        command.EventId,
        command.TargetId);
}

public static class CompetitionWebhookEventTypes
{
    public static bool IsBloodAward(CompetitionEventKind kind) => kind is
        CompetitionEventKind.FirstBloodAwarded
        or CompetitionEventKind.SecondBloodAwarded
        or CompetitionEventKind.ThirdBloodAwarded;

    public static string? From(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionLifecycleChanged =>
            "com.noctf.competition.lifecycle.changed.v1",
        CompetitionEventKind.ChallengePublished =>
            "com.noctf.competition.challenge.published.v1",
        CompetitionEventKind.ChallengeDescriptionUpdated =>
            "com.noctf.competition.challenge.updated.v1",
        CompetitionEventKind.HintPublished =>
            "com.noctf.competition.hint.published.v1",
        CompetitionEventKind.AnnouncementPublished =>
            "com.noctf.competition.announcement.published.v1",
        CompetitionEventKind.TeamBanned =>
            "com.noctf.competition.team.banned.v1",
        CompetitionEventKind.TeamBanCorrectionPublished =>
            "com.noctf.competition.team.ban.corrected.v1",
        CompetitionEventKind.FirstBloodAwarded
            or CompetitionEventKind.SecondBloodAwarded
            or CompetitionEventKind.ThirdBloodAwarded =>
            "com.noctf.competition.blood.awarded.v1",
        CompetitionEventKind.AwdpBreakResolved =>
            "com.noctf.competition.awdp.break.resolved.v1",
        CompetitionEventKind.AwdpFixResolved =>
            "com.noctf.competition.awdp.fix.resolved.v1",
        _ => null
    };

    public static bool RequiresLeaderboard(CompetitionEventKind kind) => kind is
        CompetitionEventKind.TeamBanned
        or CompetitionEventKind.TeamBanCorrectionPublished
        or CompetitionEventKind.FirstBloodAwarded
        or CompetitionEventKind.SecondBloodAwarded
        or CompetitionEventKind.ThirdBloodAwarded
        or CompetitionEventKind.AwdpBreakResolved
        or CompetitionEventKind.AwdpFixResolved;

    public static LeaderboardBloodRank? Award(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.FirstBloodAwarded => LeaderboardBloodRank.First,
        CompetitionEventKind.SecondBloodAwarded => LeaderboardBloodRank.Second,
        CompetitionEventKind.ThirdBloodAwarded => LeaderboardBloodRank.Third,
        _ => null
    };
}

internal static class BloodWebhookContract
{
    public static void Validate(
        ReadOnlyMemory<byte> body,
        CompetitionEvent source,
        LeaderboardDataScope expectedScope)
    {
        if (expectedScope == LeaderboardDataScope.Hidden)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.BlackoutSuppressed);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        if (!data.TryGetProperty("eventSequence", out var eventSequence)
            || eventSequence.ValueKind != JsonValueKind.Number
            || eventSequence.GetInt64() <= 0
            || !data.TryGetProperty("requiredProjectionVersion", out var requiredVersion)
            || requiredVersion.ValueKind != JsonValueKind.Number
            || requiredVersion.GetInt64() < 0
            || !data.TryGetProperty("publicProjectionVersion", out var publicVersion)
            || publicVersion.ValueKind != JsonValueKind.Number
            || publicVersion.GetInt64() < 0
            || !data.TryGetProperty("competitionRevision", out var revision)
            || revision.ValueKind != JsonValueKind.String
            || revision.GetGuid() == Guid.Empty)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingEventIdentity);
        var eventResource = data.GetProperty("event");
        var resources = data.GetProperty("resources");
        if (source.TeamId is not Guid teamId || teamId == Guid.Empty
            || source.CompetitionChallengeId is not Guid challengeId
            || challengeId == Guid.Empty
            || !eventResource.TryGetProperty("teamId", out var eventTeam)
            || eventTeam.ValueKind != JsonValueKind.String
            || eventTeam.GetGuid() != teamId
            || !eventResource.TryGetProperty("competitionChallengeId", out var eventChallenge)
            || eventChallenge.ValueKind != JsonValueKind.String
            || eventChallenge.GetGuid() != challengeId
            || !eventResource.TryGetProperty("award", out var award)
            || award.ValueKind != JsonValueKind.String
            || award.GetString() != CompetitionWebhookEventTypes.Award(source.Kind)?.ToString())
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingEventIdentity);

        if (!resources.TryGetProperty("challenge", out var challenge)
            || challenge.ValueKind != JsonValueKind.Object
            || !challenge.TryGetProperty("id", out var resourceId)
            || resourceId.ValueKind != JsonValueKind.String
            || resourceId.GetGuid() != challengeId)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicChallenge);
        if (!resources.TryGetProperty("leaderboard", out var leaderboard)
            || leaderboard.ValueKind != JsonValueKind.Object
            || !leaderboard.TryGetProperty("dataScope", out var scope)
            || scope.GetString() != expectedScope.ToString()
            || !leaderboard.TryGetProperty("teams", out var teams)
            || teams.ValueKind != JsonValueKind.Array)
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);

        if (!challenge.TryGetProperty("title", out var title)
            || string.IsNullOrWhiteSpace(title.GetString()))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicChallenge);
        if (!leaderboard.TryGetProperty("projectionVersion", out var projectionVersion)
            || projectionVersion.ValueKind != JsonValueKind.String
            || !long.TryParse(projectionVersion.GetString(),
                NumberStyles.None, CultureInfo.InvariantCulture,
                out var publishedVersion)
            || publicVersion.GetInt64() != publishedVersion
            || !challenge.TryGetProperty("projectionVersion", out var challengeVersion)
            || challengeVersion.GetString() != projectionVersion.GetString()
            || expectedScope == LeaderboardDataScope.Live
                && publishedVersion < requiredVersion.GetInt64())
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
        if (!teams.EnumerateArray().Any(team =>
                team.TryGetProperty("teamId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetGuid() == teamId
                && team.TryGetProperty("teamName", out var name)
                && !string.IsNullOrWhiteSpace(name.GetString())))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicTeam);
        if (expectedScope == LeaderboardDataScope.Frozen
            && (!leaderboard.TryGetProperty("dataAsOf", out var dataAsOf)
                || dataAsOf.ValueKind != JsonValueKind.String))
            throw new CompetitionWebhookProjectionNotReadyException(
                CompetitionWebhookProjectionFailure.MissingPublicProjection);
    }
}

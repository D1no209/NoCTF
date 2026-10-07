using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Competitions.StaffWebhooks;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Webhooks;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

public sealed class StaffWebhookStore(NoCtfDbContext db, PlatformSecretProtector secrets,
    CompetitionWebhookOptions options, TimeProvider clock) : IStaffWebhookStore
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(8)];
    private DateTimeOffset Now => clock.GetUtcNow();
    private async Task<bool> AuthorizedAsync(Guid actorId, Guid competitionId, bool manage, CancellationToken ct)
    {
        var active = await db.Users.AsNoTracking().Where(value => value.Id == actorId && value.AccountStatus == UserAccountStatus.Active)
            .Select(value => (UserRole?)value.Role).SingleOrDefaultAsync(ct);
        if (active is null) return false;
        return await db.Competitions.AsNoTracking().AnyAsync(value => value.Id == competitionId && value.DeletedAt == null
            && (active == UserRole.Administrator || value.OwnerId == actorId || value.Collaborators.Any(member => member.UserId == actorId
                && (member.Role == CompetitionCollaboratorRole.Manager || !manage && (member.Role == CompetitionCollaboratorRole.Judge
                    || member.Role == CompetitionCollaboratorRole.Observer)))), ct);
    }
    public async Task<OperationResult<StaffWebhookTargetPage, StaffWebhookFailure>> ListAsync(Guid competitionId, Guid actorId, int offset, int limit, CancellationToken ct, bool descending = true)
    {
        if (!await AuthorizedAsync(actorId, competitionId, false, ct)) return Fail<StaffWebhookTargetPage>(StaffWebhookFailure.Forbidden);
        var manage = await AuthorizedAsync(actorId, competitionId, true, ct);
        var query = db.StaffWebhookTargets.AsNoTracking().Where(value => value.CompetitionId == competitionId && !value.Deleted);
        var total = await query.CountAsync(ct);
        var ordered = descending ? query.OrderByDescending(value => value.UpdatedAt) : query.OrderBy(value => value.UpdatedAt);
        var rows = await ordered.ThenBy(value => value.Id).Skip(offset).Take(limit).ToArrayAsync(ct);
        return Ok(new StaffWebhookTargetPage(rows.Select(value => View(value, manage)).ToArray(), total, manage));
    }
    public Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> SaveAsync(SaveStaffWebhook command, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await AuthorizedAsync(command.ActorId, command.CompetitionId, true, ct)) return Fail<StaffWebhookMutation>(StaffWebhookFailure.Forbidden);
        var name = command.Name.Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl)) return Fail<StaffWebhookMutation>(StaffWebhookFailure.InvalidName);
        if (options.PublicBaseUrl.Scheme != "https" || !Uri.TryCreate(command.EndpointUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsoluteUri.Length > 2048) return Fail<StaffWebhookMutation>(StaffWebhookFailure.InvalidEndpoint);
        if (command.Categories.Count == 0 || command.Categories.Any(value => !Enum.IsDefined(value))
            || command.Categories.Distinct().Count() != command.Categories.Count) return Fail<StaffWebhookMutation>(StaffWebhookFailure.InvalidCategories);
        if (await db.StaffWebhookTargets.AnyAsync(value => value.CompetitionId == command.CompetitionId && !value.Deleted
                && value.Id != command.TargetId && value.EndpointUrl == uri.AbsoluteUri, ct)) return Fail<StaffWebhookMutation>(StaffWebhookFailure.DuplicateEndpoint);
        var row = command.TargetId is { } targetId ? await TargetAsync(command.CompetitionId, targetId, ct) : null;
        if (command.TargetId is not null && row is null) return Fail<StaffWebhookMutation>(StaffWebhookFailure.NotFound);
        var isNew = row is null; string? signingSecret = null;
        row ??= new() { Id = Guid.CreateVersion7(), CompetitionId = command.CompetitionId, CreatedAt = Now };
        var resync = isNew || !row.Enabled || row.AuthorizationRevoked || row.EndpointUrl != uri.AbsoluteUri
            || !row.Categories.Select(value => value.Kind).Order().SequenceEqual(command.Categories.Order());
        var disabling = row.Enabled && !command.Enabled;
        if (isNew)
        {
            signingSecret = Secret(); row.SecretCiphertext = Protect(signingSecret, row); db.StaffWebhookTargets.Add(row);
        }
        if (resync || disabling) row.Generation = Guid.NewGuid();
        row.Name = name; row.EndpointUrl = uri.AbsoluteUri; row.Enabled = command.Enabled; row.ApprovedById = command.ActorId;
        row.AuthorizationRevoked = false; row.UpdatedAt = Now;
        row.Categories.RemoveAll(value => !command.Categories.Contains(value.Kind));
        foreach (var kind in command.Categories.Where(kind => row.Categories.All(value => value.Kind != kind)))
            row.Categories.Add(new() { TargetId = row.Id, Kind = kind });
        if (disabling) await DisableEventAsync(row, ct);
        if (command.Enabled && resync)
        {
            await SnapshotAsync(row, isNew ? StaffSnapshotReason.Initial : StaffSnapshotReason.Reenabled, ct);
            row.NextHeartbeatAt = Now;
        }
        return Ok(new StaffWebhookMutation(View(row, true), signingSecret));
    }, ct);

    public Task<OperationResult<StaffWebhookMutation, StaffWebhookFailure>> RotateAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await AuthorizedAsync(actorId, competitionId, true, ct)) return Fail<StaffWebhookMutation>(StaffWebhookFailure.Forbidden);
        var row = await TargetAsync(competitionId, targetId, ct); if (row is null) return Fail<StaffWebhookMutation>(StaffWebhookFailure.NotFound);
        var secret = Secret(); row.PreviousSecretCiphertext = row.SecretCiphertext; row.PreviousSecretValidUntil = Now.AddHours(24);
        row.SecretCiphertext = Protect(secret, row); row.UpdatedAt = Now;
        return Ok(new StaffWebhookMutation(View(row, true), secret));
    }, ct);

    public async Task<StaffWebhookFailure?> DeleteAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await AuthorizedAsync(actorId, competitionId, true, ct)) return Fail<bool>(StaffWebhookFailure.Forbidden);
            var row = await TargetAsync(competitionId, targetId, ct); if (row is null) return Fail<bool>(StaffWebhookFailure.NotFound);
            row.Enabled = false; row.Deleted = true; row.Generation = Guid.NewGuid(); row.UpdatedAt = Now;
            await DisableEventAsync(row, ct); return Ok(true);
        }, ct);
        return result.FailureCode;
    }
    public Task<OperationResult<Guid, StaffWebhookFailure>> TestAsync(Guid competitionId, Guid targetId, Guid actorId, CancellationToken ct) => TransactionAsync(async () =>
    {
        if (!await AuthorizedAsync(actorId, competitionId, true, ct)) return Fail<Guid>(StaffWebhookFailure.Forbidden);
        var target = await TargetAsync(competitionId, targetId, ct); if (target is null) return Fail<Guid>(StaffWebhookFailure.NotFound);
        var stream = await StaffWebhookEventCapture.StreamAsync(db, competitionId, ct);
        var row = await AddEventAsync(stream, StaffWebhookEventKind.Test, target.Id, ct);
        AddDelivery(row, target); row.DispatchedAt = Now; return Ok(row.Id);
    }, ct);

    public async Task<OperationResult<StaffWebhookDeliveryPage, StaffWebhookFailure>> DiagnosticsAsync(Guid competitionId, Guid? targetId, Guid actorId, int offset, int limit, CancellationToken ct, bool descending = true)
    {
        if (!await AuthorizedAsync(actorId, competitionId, false, ct)) return Fail<StaffWebhookDeliveryPage>(StaffWebhookFailure.Forbidden);
        var query = db.StaffWebhookDeliveries.AsNoTracking().Where(value => value.CompetitionId == competitionId && (targetId == null || value.TargetId == targetId));
        var total = await query.CountAsync(ct);
        var joined = query.Join(db.StaffWebhookEvents, delivery => delivery.EventId, item => item.Id, (delivery, item) => new { delivery, item.Kind, item.Sequence });
        var ordered = descending ? joined.OrderByDescending(value => value.Sequence) : joined.OrderBy(value => value.Sequence);
        var items = await ordered.ThenBy(value => value.delivery.TargetId).Skip(offset).Take(limit)
            .Select(value => new StaffWebhookDeliveryView(value.delivery.EventId, value.delivery.TargetId, value.Kind, value.Sequence,
                value.delivery.State, value.delivery.CreatedAt, value.delivery.CompletedAt, value.delivery.NextAttemptAt, value.delivery.Attempts, value.delivery.LastStatusCode)).ToArrayAsync(ct);
        return Ok(new StaffWebhookDeliveryPage(items, total));
    }

    public async Task<OperationResult<StaffWebhookDeliveryView, StaffWebhookFailure>> TestStatusAsync(Guid competitionId, Guid targetId, Guid eventId, Guid actorId, CancellationToken ct)
    {
        if (!await AuthorizedAsync(actorId, competitionId, false, ct)) return Fail<StaffWebhookDeliveryView>(StaffWebhookFailure.Forbidden);
        var row = await db.StaffWebhookDeliveries.AsNoTracking().Where(value => value.CompetitionId == competitionId && value.TargetId == targetId && value.EventId == eventId)
            .Join(db.StaffWebhookEvents.Where(value => value.Kind == StaffWebhookEventKind.Test), delivery => delivery.EventId, item => item.Id,
                (delivery, item) => new StaffWebhookDeliveryView(delivery.EventId, delivery.TargetId, item.Kind, item.Sequence, delivery.State,
                    delivery.CreatedAt, delivery.CompletedAt, delivery.NextAttemptAt, delivery.Attempts, delivery.LastStatusCode)).SingleOrDefaultAsync(ct);
        return row is null ? Fail<StaffWebhookDeliveryView>(StaffWebhookFailure.NotFound) : Ok(row);
    }
    public async Task<OperationResult<StaffWorkItemPage, StaffWebhookFailure>> WorkItemsAsync(Guid competitionId, Guid actorId, int offset, int limit, bool pendingOnly, CancellationToken ct, bool descending = false)
    {
        if (!await AuthorizedAsync(actorId, competitionId, false, ct)) return Fail<StaffWorkItemPage>(StaffWebhookFailure.Forbidden);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var current = await StaffWorkItemProjection.ReadAsync(db, competitionId, ct);
        var eligible = current.Where(value => !pendingOnly || value.RequiresStaffAction);
        var ordered = descending ? eligible.OrderByDescending(value => value.ActionRequiredSince ?? value.UpdatedAt) : eligible.OrderBy(value => value.ActionRequiredSince ?? value.UpdatedAt);
        var filtered = ordered.ThenBy(value => value.Id).ToArray();
        var items = filtered.Skip(offset).Take(limit).Select(value => new StaffWorkItemView(value.Kind, value.Id, value.CheatStatus,
            value.ConsultationStatus, value.AppealStatus, value.RequiresStaffAction, value.ActionRequiredSince, value.CreatedAt, value.UpdatedAt,
            value.TeamId, value.TeamName, value.ChallengeId, value.ChallengeTitle, value.ReasonCode, StaffWebhookProtocol.ManagementUrl(value.Kind, value.Id, competitionId, options.PublicBaseUrl))).ToArray();
        await transaction.CommitAsync(ct); return Ok(new StaffWorkItemPage(items, filtered.Length));
    }
    public async Task TickAsync(bool recovering, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            var targets = await db.StaffWebhookTargets.Where(value => value.Enabled && !value.Deleted
                && (recovering || value.NextHeartbeatAt <= Now)).OrderBy(value => value.NextHeartbeatAt).Take(recovering ? int.MaxValue : 64).ToArrayAsync(ct);
            foreach (var target in targets)
            {
                if (target.FailureSince <= Now.AddSeconds(-180)) target.NeedsResync = true;
                if (!await AuthorizedAsync(target.ApprovedById, target.CompetitionId, true, ct))
                {
                    target.Enabled = false; target.AuthorizationRevoked = true; target.Generation = Guid.NewGuid(); target.UpdatedAt = Now;
                    await DisableEventAsync(target, ct); continue;
                }
                if (recovering && target.ActiveSnapshotId is null) await SnapshotAsync(target, StaffSnapshotReason.Resync, ct);
                var stream = await StaffWebhookEventCapture.StreamAsync(db, target.CompetitionId, ct);
                var heartbeat = await AddEventAsync(stream, StaffWebhookEventKind.Heartbeat, target.Id, ct);
                AddDelivery(heartbeat, target); heartbeat.DispatchedAt = Now; target.NextHeartbeatAt = Now.AddSeconds(60);
            }
            var events = await db.StaffWebhookEvents.Where(value => value.DispatchedAt == null).OrderBy(value => value.OccurredAt).ThenBy(value => value.Sequence).Take(64).ToArrayAsync(ct);
            foreach (var item in events)
            {
                var recipients = await db.StaffWebhookTargets.Where(value => value.CompetitionId == item.CompetitionId && !value.Deleted
                    && value.Enabled && (item.TargetId == null || value.Id == item.TargetId)).ToArrayAsync(ct);
                foreach (var target in recipients.Where(target => item.Kind is not (StaffWebhookEventKind.WorkItemCreated or StaffWebhookEventKind.WorkItemUpdated)
                    || item.Items.Count == 1 && target.Categories.Any(value => value.Kind == item.Items[0].Summary.Kind))) AddDelivery(item, target);
                item.DispatchedAt = Now;
            }
            return Ok(true);
        }, ct);
        if (!result.Succeeded) throw new DbUpdateConcurrencyException("Staff webhook recovery scan conflicted.");
    }
    public async Task<IReadOnlyList<DeliverStaffWebhook>> ClaimAsync(int limit, CancellationToken ct)
    {
        var now = Now;
        var candidates = await db.StaffWebhookDeliveries.AsNoTracking().Where(value => value.State == StaffWebhookDeliveryState.Pending && value.NextAttemptAt <= now
                || value.State == StaffWebhookDeliveryState.InFlight && value.LeaseUntil <= now)
            .OrderBy(value => value.NextAttemptAt).Take(limit).ToArrayAsync(ct);
        var claimed = new List<DeliverStaffWebhook>();
        foreach (var row in candidates)
        {
            var attemptToken = Guid.NewGuid();
            var updated = await db.StaffWebhookDeliveries.Where(value => value.EventId == row.EventId && value.TargetId == row.TargetId
                    && value.ConcurrencyStamp == row.ConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.State, StaffWebhookDeliveryState.InFlight)
                    .SetProperty(value => value.LeaseUntil, now.AddMinutes(2)).SetProperty(value => value.ConcurrencyStamp, Guid.NewGuid())
                    .SetProperty(value => value.AttemptToken, attemptToken).SetProperty(value => value.PreparedAt, (DateTimeOffset?)null), ct);
            if (updated == 1) claimed.Add(new(row.CompetitionId, row.EventId, row.TargetId, attemptToken));
        }
        return claimed;
    }
    public async Task<StaffWebhookPreparedDelivery> PrepareAsync(DeliverStaffWebhook command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            var row = await DeliveryAsync(command, ct); if (row is null || row.State != StaffWebhookDeliveryState.InFlight || row.PreparedAt is not null) return Ok(new StaffWebhookPreparedDelivery(null));
            var target = await db.StaffWebhookTargets.SingleOrDefaultAsync(value => value.Id == command.TargetId && value.CompetitionId == command.CompetitionId, ct);
            var item = await db.StaffWebhookEvents.SingleAsync(value => value.Id == command.EventId, ct);
            var control = item.Kind == StaffWebhookEventKind.SubscriptionDisabled;
            var test = item.Kind == StaffWebhookEventKind.Test;
            var authorized = target is not null && await AuthorizedAsync(target.ApprovedById, target.CompetitionId, true, ct);
            var suppressed = target is null || row.Generation != target.Generation || !control && (!authorized || target.Deleted || !target.Enabled && !test)
                || item.Kind is StaffWebhookEventKind.WorkItemCreated or StaffWebhookEventKind.WorkItemUpdated
                    && (item.Sequence <= target!.SyncedThrough || !target.Categories.Any(value => value.Kind == item.Items.Single().Summary.Kind))
                || item.Kind == StaffWebhookEventKind.PendingSnapshot && item.SnapshotId != target!.ActiveSnapshotId;
            if (suppressed) { row.State = StaffWebhookDeliveryState.Suppressed; row.CompletedAt = Now; return Ok(new StaffWebhookPreparedDelivery(null)); }
            if (item.Kind is StaffWebhookEventKind.WorkItemCreated or StaffWebhookEventKind.WorkItemUpdated or StaffWebhookEventKind.PendingSnapshot
                && (target!.NeedsResync || target.ActiveSnapshotId is not null && item.Kind != StaffWebhookEventKind.PendingSnapshot))
            { row.State = StaffWebhookDeliveryState.Pending; row.NextAttemptAt = Now.AddSeconds(5); row.LeaseUntil = null; return Ok(new StaffWebhookPreparedDelivery(null, true)); }
            // The durable claim is transferred to one handler; duplicates cannot both prepare HTTP sends.
            row.LeaseUntil = Now.AddMinutes(2); row.PreparedAt = Now; row.Attempts++;
            var body = StaffWebhookProtocol.Serialize(item, target!.Id, options.PublicBaseUrl);
            if (body.Length > 1024 * 1024) { row.State = StaffWebhookDeliveryState.DeadLetter; row.CompletedAt = Now; return Ok(new StaffWebhookPreparedDelivery(null)); }
            return Ok(new StaffWebhookPreparedDelivery(new(CompetitionWebhookDeliveryReadState.Ready, command.CompetitionId, command.EventId, command.TargetId,
                new Uri(target.EndpointUrl), body, Unprotect(target.SecretCiphertext, target),
                target.PreviousSecretValidUntil > Now && target.PreviousSecretCiphertext is { } previous ? Unprotect(previous, target) : null,
                RequireNoContent: true)));
        }, ct);
        if (!result.Succeeded) throw new DbUpdateConcurrencyException("Staff webhook preparation conflicted.");
        return result.Value!;
    }
    public async Task CompleteAsync(DeliverStaffWebhook command, int? status, bool delivered, bool permanent, DateTimeOffset? retryAfter, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            var row = await DeliveryAsync(command, ct); if (row is null || row.State != StaffWebhookDeliveryState.InFlight) return Ok(true);
            var target = await db.StaffWebhookTargets.SingleAsync(value => value.Id == command.TargetId, ct);
            if (target.Generation != row.Generation) { row.State = StaffWebhookDeliveryState.Suppressed; row.CompletedAt = Now; return Ok(true); }
            var item = await db.StaffWebhookEvents.SingleAsync(value => value.Id == command.EventId, ct);
            row.LastStatusCode = status; row.LeaseUntil = null;
            if (delivered)
            {
                row.State = StaffWebhookDeliveryState.Delivered; row.CompletedAt = Now;
                if (target.NeedsResync && item.Kind == StaffWebhookEventKind.Heartbeat)
                { target.NeedsResync = false; target.FailureSince = null; await SnapshotAsync(target, StaffSnapshotReason.Resync, ct); }
                else if (item.Kind == StaffWebhookEventKind.PendingSnapshot && item.SnapshotId == target.ActiveSnapshotId)
                {
                    var pending = await db.StaffWebhookDeliveries.AnyAsync(value => value.TargetId == target.Id && value.EventId != row.EventId
                        && value.State != StaffWebhookDeliveryState.Delivered && db.StaffWebhookEvents.Any(page => page.Id == value.EventId && page.SnapshotId == item.SnapshotId), ct);
                    if (!pending) { target.ActiveSnapshotId = null; target.FailureSince = null; }
                }
                else if (!target.NeedsResync && target.ActiveSnapshotId is null) target.FailureSince = null;
            }
            else
            {
                target.FailureSince ??= Now;
                if (Now - target.FailureSince >= TimeSpan.FromSeconds(180)) target.NeedsResync = true;
                if (status == 410)
                { target.Enabled = false; target.AuthorizationRevoked = false; target.Generation = Guid.NewGuid(); await DisableEventAsync(target, ct); }
                if (permanent || Now - row.CreatedAt >= TimeSpan.FromHours(24))
                { row.State = StaffWebhookDeliveryState.DeadLetter; row.CompletedAt = Now; target.NeedsResync = true; }
                else
                {
                    row.State = StaffWebhookDeliveryState.Pending;
                    var retry = retryAfter ?? Now.Add(RetryDelays[Math.Min(Math.Max(0, row.Attempts - 1), RetryDelays.Length - 1)]);
                    row.NextAttemptAt = retry <= Now ? Now.AddSeconds(1) : retry > row.CreatedAt.AddHours(24) ? row.CreatedAt.AddHours(24) : retry;
                }
            }
            return Ok(true);
        }, ct);
        if (!result.Succeeded) throw new DbUpdateConcurrencyException("Staff webhook completion conflicted.");
    }
    private async Task SnapshotAsync(StaffWebhookTarget target, StaffSnapshotReason reason, CancellationToken ct)
    {
        await StaffWebhookEventCapture.CaptureAsync(db, [target.CompetitionId], Now, ct, force: true);
        var stream = await StaffWebhookEventCapture.StreamAsync(db, target.CompetitionId, ct);
        // Include unsaved projections produced by initial capture.
        var stored = await db.StaffWebhookWorkItems.Where(value => value.CompetitionId == target.CompetitionId).ToArrayAsync(ct);
        var all = stored.Concat(db.StaffWebhookWorkItems.Local.Where(value => value.CompetitionId == target.CompetitionId)).DistinctBy(value => (value.Kind, value.ItemId));
        var items = all.Where(value => value.Summary.RequiresStaffAction && target.Categories.Any(category => category.Kind == value.Kind))
            .OrderBy(value => value.Summary.ActionRequiredSince).ThenBy(value => value.ItemId).Select(value => value.Summary.Copy()).ToArray();
        var id = Guid.CreateVersion7(); var asOf = stream.Sequence; var pages = Math.Max(1, (items.Length + 99) / 100);
        target.ActiveSnapshotId = id; target.SyncedThrough = asOf;
        for (var index = 0; index < pages; index++)
        {
            var row = await AddEventAsync(stream, StaffWebhookEventKind.PendingSnapshot, target.Id, ct, items.Skip(index * 100).Take(100).ToArray());
            row.SnapshotId = id; row.SnapshotReason = reason; row.AsOfSequence = asOf; row.PageIndex = index; row.PageCount = pages;
            AddDelivery(row, target); row.DispatchedAt = Now;
        }
    }
    private async Task DisableEventAsync(StaffWebhookTarget target, CancellationToken ct)
    {
        target.ActiveSnapshotId = null;
        var stream = await StaffWebhookEventCapture.StreamAsync(db, target.CompetitionId, ct);
        var row = await AddEventAsync(stream, StaffWebhookEventKind.SubscriptionDisabled, target.Id, ct);
        AddDelivery(row, target); row.DispatchedAt = Now;
    }
    private async Task<StaffWebhookEvent> AddEventAsync(StaffWebhookStream stream, StaffWebhookEventKind kind, Guid targetId, CancellationToken ct,
        IReadOnlyList<StaffWorkItemSummary>? items = null)
    {
        var row = StaffWebhookEventCapture.AddEvent(db, stream, kind, Now, items: items, targetId: targetId);
        row.CompetitionTitle = await db.Competitions.IgnoreQueryFilters().Where(value => value.Id == stream.CompetitionId).Select(value => value.Title).SingleAsync(ct);
        return row;
    }
    private void AddDelivery(StaffWebhookEvent row, StaffWebhookTarget target) => db.StaffWebhookDeliveries.Add(new()
    { EventId = row.Id, TargetId = target.Id, CompetitionId = row.CompetitionId, Generation = target.Generation,
        State = StaffWebhookDeliveryState.Pending, CreatedAt = Now, NextAttemptAt = Now });
    private Task<StaffWebhookTarget?> TargetAsync(Guid competitionId, Guid id, CancellationToken ct) => db.StaffWebhookTargets
        .SingleOrDefaultAsync(value => value.Id == id && value.CompetitionId == competitionId && !value.Deleted, ct);
    private Task<StaffWebhookDelivery?> DeliveryAsync(DeliverStaffWebhook command, CancellationToken ct) => db.StaffWebhookDeliveries.SingleOrDefaultAsync(value =>
        value.EventId == command.EventId && value.TargetId == command.TargetId && value.CompetitionId == command.CompetitionId && value.AttemptToken == command.AttemptToken, ct);
    private byte[] Protect(string value, StaffWebhookTarget target) => secrets.Protect(value, PlatformSecretPurpose.StaffWebhookSecret, target.CompetitionId, target.Id);
    private string Unprotect(byte[] value, StaffWebhookTarget target) => secrets.Unprotect(value, PlatformSecretPurpose.StaffWebhookSecret, target.CompetitionId, target.Id);
    private static string Secret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return "whsec_" + Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static StaffWebhookTargetView View(StaffWebhookTarget value, bool manage) => new(value.Id, value.Name, manage ? value.EndpointUrl : null,
        value.Enabled, value.AuthorizationRevoked, value.Categories.Select(category => category.Kind).Order().ToArray(), value.CreatedAt, value.UpdatedAt,
        manage ? value.PreviousSecretValidUntil : null, value.ActiveSnapshotId is not null || value.NeedsResync, value.FailureSince);
    private async Task<OperationResult<T, StaffWebhookFailure>> TransactionAsync<T>(Func<Task<OperationResult<T, StaffWebhookFailure>>> action, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await action(); if (!result.Succeeded) return result;
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
            }
            catch (Exception exception) when (RelationalRetry.IsTransientConcurrency(exception)) { }
        }
        return Fail<T>(StaffWebhookFailure.Conflict);
    }
    private static OperationResult<T, StaffWebhookFailure> Ok<T>(T value) => OperationResult<T, StaffWebhookFailure>.Success(value);
    private static OperationResult<T, StaffWebhookFailure> Fail<T>(StaffWebhookFailure failure) => OperationResult<T, StaffWebhookFailure>.Failure(failure, "Staff webhook operation failed.");
}

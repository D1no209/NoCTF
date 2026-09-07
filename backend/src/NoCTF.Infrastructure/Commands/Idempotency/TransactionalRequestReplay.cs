using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Commands.Idempotency;

/// <summary>The receipt is saved in the business transaction, not in a volatile HTTP cache.</summary>
public sealed class TransactionalRequestReplay(NoCtfDbContext db, IRequestCommandKey? request = null, TimeProvider? clock = null) : IRequestReplay
{
    private Guid receiptId;
    private string? fingerprint;
    private ReplayScope? scope;
    private Type? resultType;
    public bool Replayed { get; private set; }
    public Guid? ActorId => request?.ActorId;

    public async Task<T?> FindAsync<T>(ReplayScope commandScope, object input, CancellationToken ct) where T : class
    {
        if (request?.Key is not Guid key) return null;
        scope = commandScope;
        resultType = typeof(T);
        receiptId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"http-command:{scope.UserId:N}:{(int)scope.Operation}:{scope.CompetitionId:N}:{scope.ResourceId:N}:{key:N}")).AsSpan(0, 16));
        fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(2));
            try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({receiptId.ToString("N")}, 11))", budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new FeatureCriticalSectionTimeoutException("http-command"); }
        }
        var receipt = await db.Notifications.AsNoTracking().Where(item => item.Id == receiptId).SingleOrDefaultAsync(ct);
        if (receipt is null) return null;
        using var json = JsonDocument.Parse(receipt.ContentJson);
        if (receipt.Kind != NotificationKind.HttpCommandReceipt || receipt.SourceId != scope.UserId
            || json.RootElement.GetProperty("fingerprint").GetString() != fingerprint)
            throw new RequestReplayConflictException();
        Replayed = true;
        return json.RootElement.GetProperty("result").Deserialize<T>()
            ?? throw new InvalidOperationException("The committed command receipt cannot be read.");
    }

    public void Store<T>(T response) where T : class
    {
        if (request?.Key is null) return;
        if (scope is null || resultType != typeof(T) || db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A command receipt must match the initialized request and share its business transaction.");
        db.Notifications.Add(new Notification
        {
            Id = receiptId, Kind = NotificationKind.HttpCommandReceipt,
            SourceType = NotificationSourceType.User, SourceId = scope.UserId,
            TargetType = NotificationTargetType.User, TargetId = scope.UserId,
            RelatedType = scope.CompetitionId == Guid.Empty ? EntityReferenceKind.Challenge : EntityReferenceKind.Competition,
            RelatedId = scope.CompetitionId == Guid.Empty ? scope.ResourceId : scope.CompetitionId,
            SentAt = (clock ?? TimeProvider.System).GetUtcNow(),
            ContentJson = JsonSerializer.Serialize(new { schemaVersion = 1, fingerprint, result = response })
        });
    }
}

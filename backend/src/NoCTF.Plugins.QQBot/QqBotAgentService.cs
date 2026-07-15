using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.QqBot;
using NoCTF.Core;
using NoCTF.Infrastructure;
using StackExchange.Redis;

namespace NoCTF.Plugins.QQBot;

internal sealed class QqBotAgentService(
    ApplicationDbContext db,
    IConnectionMultiplexer redis) : IQqBotAgentService
{
    private static readonly HashSet<string> RetryableErrors = new(StringComparer.Ordinal)
    {
        "transport_timeout", "bot_offline", "remote_5xx", "rate_limited"
    };
    private static readonly HashSet<string> PermanentErrors = new(StringComparer.Ordinal)
    {
        "group_not_found", "bot_not_in_group", "permission_denied", "invalid_message",
        "authentication_failed", "delivery_outcome_unknown"
    };

    public bool IsAvailable => true;

    public async Task<QqBotAgentAuthenticationResult> AuthenticateAsync(
        HttpContext context,
        ReadOnlyMemory<byte> body,
        CancellationToken ct = default)
    {
        if (!TryHeader(context, "X-NoCTF-Agent-Id", out var agentText) || !Guid.TryParse(agentText, out var agentId) ||
            !TryHeader(context, "X-NoCTF-Timestamp", out var timestampText) || !long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) ||
            !TryHeader(context, "X-NoCTF-Nonce", out var nonce) || nonce.Length is < 16 or > 128 || !nonce.All(IsBase64UrlCharacter) ||
            !TryHeader(context, "X-NoCTF-Signature", out var signatureText))
            return QqBotAgentAuthenticationResult.Failure("signature_headers_invalid");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > 60)
            return QqBotAgentAuthenticationResult.Failure("signature_timestamp_invalid");
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException) { return QqBotAgentAuthenticationResult.Failure("signature_invalid"); }

        var agent = await db.QqBotAgents.AsNoTracking().FirstOrDefaultAsync(item => item.Id == agentId && item.Enabled, ct);
        if (agent is null)
            return QqBotAgentAuthenticationResult.Failure("agent_unknown_or_disabled");
        var bodyHash = Convert.ToHexString(SHA256.HashData(body.Span)).ToLowerInvariant();
        var canonical = string.Join('\n', "NOCTF-QQBOT-V1", context.Request.Method.ToUpperInvariant(),
            context.Request.Path.Value ?? "/", timestampText, nonce, bodyHash);
        var data = Encoding.UTF8.GetBytes(canonical);
        if (!Verify(agent.PublicKeyPem, data, signature) &&
            !(agent.PreviousKeyValidUntil >= DateTime.UtcNow &&
              !string.IsNullOrWhiteSpace(agent.PreviousPublicKeyPem) &&
              Verify(agent.PreviousPublicKeyPem, data, signature)))
            return QqBotAgentAuthenticationResult.Failure("signature_invalid");

        var nonceKey = $"noctf:qqbot:nonce:{agentId:N}:{nonce}";
        if (!await redis.GetDatabase().StringSetAsync(nonceKey, "1", TimeSpan.FromMinutes(2), When.NotExists))
            return QqBotAgentAuthenticationResult.Failure("signature_replay");
        return QqBotAgentAuthenticationResult.Success(agentId);
    }

    public async Task<QqBotAgentOperationResult> HeartbeatAsync(Guid agentId, QqBotHeartbeatRequest request, CancellationToken ct = default)
    {
        var agent = await db.QqBotAgents.FirstOrDefaultAsync(item => item.Id == agentId && item.Enabled, ct);
        if (agent is null) return new(false, "rejected", "agent_unknown_or_disabled");
        agent.QqOnline = request.QqOnline;
        agent.BotUin = request.BotUin is > 0 ? request.BotUin : null;
        agent.BotNickname = SafeText(request.BotNickname, 120);
        agent.ImplementationName = SafeText(request.ImplementationName, 120);
        agent.ImplementationVersion = SafeText(request.ImplementationVersion, 80);
        agent.MilkyVersion = SafeText(request.MilkyVersion, 80);
        agent.LastErrorCode = SafeCode(request.ErrorCode);
        agent.LastErrorSummary = SafeText(request.ErrorSummary, 500);
        agent.LastHeartbeatAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(true, "accepted");
    }

    public async Task<QqBotGroupSyncResult> SyncGroupsAsync(Guid agentId, QqBotGroupSyncRequest request, CancellationToken ct = default)
    {
        if (request.Groups.Count > 1000 || request.Groups.Any(item => item.GroupId <= 0))
            throw new ArgumentException("group_list_invalid");
        var agent = await db.QqBotAgents.FirstOrDefaultAsync(item => item.Id == agentId && item.Enabled, ct)
            ?? throw new InvalidOperationException("agent_unknown_or_disabled");
        var unique = request.Groups.GroupBy(item => item.GroupId).Select(group => group.First()).ToArray();
        var existing = await db.QqBotGroups.Where(item => item.AgentId == agentId).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var group in existing) group.IsPresent = false;
        foreach (var item in unique)
        {
            var group = existing.FirstOrDefault(existingGroup => existingGroup.GroupId == item.GroupId);
            if (group is null)
            {
                group = new QqBotGroup { Id = Guid.NewGuid(), AgentId = agentId, GroupId = item.GroupId };
                db.QqBotGroups.Add(group);
            }
            group.GroupName = SafeText(item.GroupName, 160) ?? item.GroupId.ToString(CultureInfo.InvariantCulture);
            group.IsPresent = true;
            group.LastSeenAt = now;
            group.UpdatedAt = now;
        }
        agent.LastGroupSyncAt = now;
        agent.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new QqBotGroupSyncResult(unique.Length, await db.QqBotGroups.CountAsync(item => item.AgentId == agentId && item.IsPresent && item.IsAuthorized, ct));
    }

    public async Task<QqBotLeaseResponse> LeaseAsync(Guid agentId, QqBotLeaseRequest request, CancellationToken ct = default)
    {
        var global = await db.QqBotGlobalSettings.AsNoTracking().FirstOrDefaultAsync(item => item.Id == QqBotDefaults.GlobalSettingsId, ct);
        if (global is null || !global.Enabled) return new(null, 1000);
        var waitSeconds = Math.Clamp(request.WaitSeconds, 0, global.LongPollSeconds);
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        do
        {
            var lease = await TryLeaseAsync(agentId, global, ct);
            if (lease is not null) return new(lease, 0);
            if (DateTime.UtcNow >= deadline) break;
            await Task.Delay(250, ct);
        } while (!ct.IsCancellationRequested);
        return new(null, 500);
    }

    public async Task<QqBotAgentOperationResult> AcknowledgeAsync(Guid agentId, QqBotDeliveryAckRequest request, CancellationToken ct = default)
    {
        var delivery = await db.QqBotDeliveries.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == request.DeliveryId && item.AgentId == agentId, ct);
        if (delivery is null) return new(false, "rejected", "delivery_not_found");
        if (delivery.Status == QqBotDeliveryStatus.Succeeded) return new(true, "already_acknowledged");
        if (delivery.Status != QqBotDeliveryStatus.Leased || delivery.LeaseToken != request.LeaseToken)
            return new(false, "rejected", "lease_invalid");
        var now = DateTime.UtcNow;
        delivery.Status = QqBotDeliveryStatus.Succeeded;
        delivery.SentAt = now;
        delivery.RemoteMessageSequence = request.MessageSequence;
        delivery.RemoteSentAt = request.RemoteSentAt;
        delivery.SafeRemoteSummary = SafeText(request.SafeRemoteSummary, 500);
        delivery.LeaseToken = null;
        delivery.LockedUntil = null;
        delivery.LastErrorCode = null;
        delivery.LastErrorSummary = null;
        delivery.LastErrorRetryable = null;
        delivery.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new(true, "acknowledged");
    }

    public async Task<QqBotAgentOperationResult> FailAsync(Guid agentId, QqBotDeliveryFailureRequest request, CancellationToken ct = default)
    {
        var delivery = await db.QqBotDeliveries.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == request.DeliveryId && item.AgentId == agentId, ct);
        if (delivery is null) return new(false, "rejected", "delivery_not_found");
        if (delivery.Status is QqBotDeliveryStatus.Succeeded or QqBotDeliveryStatus.Failed)
            return new(true, delivery.Status == QqBotDeliveryStatus.Succeeded ? "already_acknowledged" : "already_failed");
        if (delivery.Status != QqBotDeliveryStatus.Leased || delivery.LeaseToken != request.LeaseToken)
            return new(false, "rejected", "lease_invalid");

        var code = SafeCode(request.ErrorCode) ?? "unknown";
        var retryable = RetryableErrors.Contains(code);
        if (!retryable && !PermanentErrors.Contains(code)) code = "unknown";
        var now = DateTime.UtcNow;
        delivery.LastErrorCode = code;
        delivery.LastErrorSummary = SafeText(request.SafeSummary, 500);
        delivery.LastErrorRetryable = retryable;
        delivery.LeaseToken = null;
        delivery.LockedUntil = null;
        delivery.UpdatedAt = now;
        if (retryable && delivery.AttemptCount < delivery.MaxAttempts)
        {
            delivery.Status = QqBotDeliveryStatus.Retrying;
            delivery.AvailableAt = now.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Max(0, delivery.AttemptCount - 1))));
            await db.SaveChangesAsync(ct);
            return new(true, "retry_scheduled", code);
        }
        delivery.Status = QqBotDeliveryStatus.Failed;
        await db.SaveChangesAsync(ct);
        return new(true, "failed", code);
    }

    private async Task<QqBotDeliveryLease?> TryLeaseAsync(Guid agentId, QqBotGlobalSettings global, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        await db.QqBotDeliveries
            .IgnoreQueryFilters()
            .Where(item =>
                item.AgentId == agentId &&
                item.Status == QqBotDeliveryStatus.Leased &&
                item.LockedUntil <= now &&
                item.AttemptCount >= item.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, QqBotDeliveryStatus.Failed)
                .SetProperty(item => item.LeaseToken, (Guid?)null)
                .SetProperty(item => item.LockedUntil, (DateTime?)null)
                .SetProperty(item => item.LastErrorCode, "delivery_outcome_unknown")
                .SetProperty(item => item.LastErrorSummary, "The final delivery lease expired without an acknowledgement.")
                .SetProperty(item => item.LastErrorRetryable, false)
                .SetProperty(item => item.UpdatedAt, now), ct);
        var delivery = await db.QqBotDeliveries
            .FromSqlInterpolated($$"""
                SELECT * FROM "QqBotDeliveries"
                WHERE "AgentId" = {{agentId}}
                  AND "AttemptCount" < "MaxAttempts"
                  AND (("Status" IN ({{QqBotDeliveryStatus.Pending}}, {{QqBotDeliveryStatus.Retrying}}) AND "AvailableAt" <= {{now}})
                       OR ("Status" = {{QqBotDeliveryStatus.Leased}} AND "LockedUntil" <= {{now}}))
                ORDER BY "AvailableAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .IgnoreQueryFilters()
            .AsTracking()
            .FirstOrDefaultAsync(ct);
        if (delivery is null)
        {
            await transaction.CommitAsync(ct);
            return null;
        }
        var redisDb = redis.GetDatabase();
        if (!await TryCooldownAsync(redisDb, $"noctf:qqbot:rate:group:{delivery.QqGroupId}", global.GroupCooldownMilliseconds) ||
            !await TryCooldownAsync(redisDb, $"noctf:qqbot:rate:competition:{delivery.CompetitionId:N}", global.CompetitionCooldownMilliseconds))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return null;
        }
        var token = Guid.NewGuid();
        var lockedUntil = now.AddSeconds(global.DeliveryLeaseSeconds);
        delivery.Status = QqBotDeliveryStatus.Leased;
        delivery.AttemptCount++;
        delivery.LeaseToken = token;
        delivery.LockedUntil = lockedUntil;
        delivery.LastAttemptAt = now;
        delivery.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new QqBotDeliveryLease(delivery.Id, token, delivery.QqGroupId, delivery.RenderedSegmentsJson,
            lockedUntil, delivery.AttemptCount, delivery.MaxAttempts);
    }

    private static async Task<bool> TryCooldownAsync(IDatabase database, string key, int milliseconds)
        => milliseconds <= 0 || await database.StringSetAsync(key, "1", TimeSpan.FromMilliseconds(milliseconds), When.NotExists);
    private static bool TryHeader(HttpContext context, string name, out string value)
    {
        value = context.Request.Headers[name].ToString();
        return !string.IsNullOrWhiteSpace(value);
    }
    private static bool IsBase64UrlCharacter(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_';
    private static bool Verify(string pem, byte[] data, byte[] signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key.KeySize == 256 && key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException) { return false; }
    }
    private static string? SafeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= 80 && normalized.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') ? normalized : "invalid_error_code";
    }
    private static string? SafeText(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Normalize(NormalizationForm.FormC)
            .Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t').ToArray()).Trim();
        return normalized.Length <= max ? normalized : normalized[..max];
    }
}

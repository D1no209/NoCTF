using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

/// <summary>One-use SSO correlation and result state, with EF-managed concurrency.</summary>
public sealed class PersistedSsoFlowStore(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider clock) : ISsoFlowStore
{
    public async Task<bool> CreateAsync(SsoFlowRecord flow, CancellationToken ct)
    {
        var ttl = flow.ExpiresAt - flow.CreatedAt;
        if (ttl <= TimeSpan.Zero || ttl > SsoRules.FlowLifetime)
            throw new ArgumentOutOfRangeException(nameof(flow), "SSO flow lifetime is invalid.");
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            await db.SsoFlows.Where(item => item.ExpiresAt <= clock.GetUtcNow())
                .ExecuteDeleteAsync(ct);
            db.SsoFlows.Add(FromRecord(flow));
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception)
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            if (await db.SsoFlows.AsNoTracking().AnyAsync(item =>
                    item.Id == flow.Id || item.CorrelationHash == Hash(flow.CorrelationToken), ct))
                return false;
            throw new SsoFlowDependencyException(exception);
        }
        catch (DbException exception)
        {
            throw new SsoFlowDependencyException(exception);
        }
    }

    public async Task<SsoFlowClaimResult> ClaimCallbackAsync(
        string correlationToken, Guid providerId, string browserIdHash,
        CancellationToken ct)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();
            var flow = await db.SsoFlows.AsNoTracking().SingleOrDefaultAsync(
                item => item.CorrelationHash == Hash(correlationToken)
                    && item.ExpiresAt > now, ct);
            if (flow is null) return new(SsoFlowClaimState.NotFound);
            if (flow.State != SsoFlowState.Pending)
                return new(SsoFlowClaimState.NotFound);
            if (flow.ProviderId != providerId || flow.BrowserIdHash != browserIdHash)
                return new(SsoFlowClaimState.InvalidCorrelation);

            var processingToken = RandomToken();
            var expiresAt = now.Add(SsoRules.ResultLifetime);
            var updated = await db.SsoFlows.Where(item => item.Id == flow.Id
                    && item.ConcurrencyStamp == flow.ConcurrencyStamp
                    && item.State == SsoFlowState.Pending && item.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.State, SsoFlowState.Processing)
                    .SetProperty(item => item.ProcessingTokenHash, Hash(processingToken))
                    .SetProperty(item => item.ExpiresAt, expiresAt)
                    .SetProperty(item => item.ConcurrencyStamp, Guid.NewGuid()), ct);
            return updated == 1
                ? new(SsoFlowClaimState.Claimed,
                    ToRecord(flow) with
                    {
                        State = SsoFlowState.Processing,
                        ProcessingToken = processingToken,
                        ExpiresAt = expiresAt
                    }, processingToken)
                : new(SsoFlowClaimState.NotFound);
        }
        catch (DbException)
        {
            return new(SsoFlowClaimState.DependencyUnavailable);
        }
    }

    public Task<bool> SaveAuthenticatedAsync(
        Guid flowId, string processingToken, SsoExternalIdentity identity,
        CancellationToken ct) => CompleteAsync(
        flowId, processingToken, SsoFlowState.Authenticated, identity, null, ct);

    public Task<bool> SaveFailureAsync(
        Guid flowId, string processingToken, SsoFailureCode failure,
        CancellationToken ct) => CompleteAsync(
        flowId, processingToken, SsoFlowState.Failed, null, failure, ct);

    private async Task<bool> CompleteAsync(
        Guid flowId, string processingToken, SsoFlowState state,
        SsoExternalIdentity? identity, SsoFailureCode? failure, CancellationToken ct)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();
            var flow = await db.SsoFlows.SingleOrDefaultAsync(item => item.Id == flowId
                && item.ExpiresAt > now, ct);
            if (flow is null || flow.State != SsoFlowState.Processing
                || flow.ProcessingTokenHash != Hash(processingToken))
                return false;
            flow.State = state;
            flow.ProcessingTokenHash = null;
            flow.ExpiresAt = now.Add(SsoRules.ResultLifetime);
            flow.ExternalProviderId = identity?.ProviderId;
            flow.ExternalProtocol = identity?.Protocol;
            flow.ExternalNamespace = identity?.IdentityNamespace;
            flow.ExternalSubject = identity?.Subject;
            flow.ExternalDisplayName = identity?.DisplayName;
            flow.FailureCode = failure;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbException)
        {
            return false;
        }
    }

    public Task<SsoFlowReadResult> ReadAsync(
        Guid flowId, string browserIdHash, CancellationToken ct) =>
        ReadOrConsumeAsync(flowId, browserIdHash, false, ct);

    public Task<SsoFlowReadResult> ConsumeAuthenticatedAsync(
        Guid flowId, string browserIdHash, CancellationToken ct) =>
        ReadOrConsumeAsync(flowId, browserIdHash, true, ct);

    private async Task<SsoFlowReadResult> ReadOrConsumeAsync(
        Guid flowId, string browserIdHash, bool consume, CancellationToken ct)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var now = clock.GetUtcNow();
            var flow = await db.SsoFlows.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == flowId && item.ExpiresAt > now, ct);
            if (flow is null) return new(SsoFlowReadState.NotFound);
            if (flow.BrowserIdHash != browserIdHash)
                return new(SsoFlowReadState.InvalidCorrelation);
            if (consume)
            {
                if (flow.State != SsoFlowState.Authenticated)
                    return new(SsoFlowReadState.NotFound);
                var deleted = await db.SsoFlows.Where(item => item.Id == flowId
                        && item.ConcurrencyStamp == flow.ConcurrencyStamp
                        && item.State == SsoFlowState.Authenticated)
                    .ExecuteDeleteAsync(ct);
                if (deleted != 1) return new(SsoFlowReadState.NotFound);
                return new(SsoFlowReadState.Available,
                    ToRecord(flow) with { State = SsoFlowState.Consumed });
            }
            return new(SsoFlowReadState.Available, ToRecord(flow));
        }
        catch (DbException)
        {
            return new(SsoFlowReadState.DependencyUnavailable);
        }
    }

    private static SsoFlowEntity FromRecord(SsoFlowRecord flow) => new()
    {
        Id = flow.Id,
        ProviderId = flow.ProviderId,
        Protocol = flow.Protocol,
        Intent = flow.Intent,
        BrowserIdHash = flow.BrowserIdHash,
        State = flow.State,
        CorrelationHash = Hash(flow.CorrelationToken),
        ProviderFingerprint = flow.ProviderFingerprint,
        ReturnPath = flow.ReturnPath,
        CreatedAt = flow.CreatedAt,
        ExpiresAt = flow.ExpiresAt,
        UserId = flow.UserId,
        TokenVersion = flow.TokenVersion,
        Nonce = flow.Nonce,
        PkceVerifier = flow.PkceVerifier,
        ServiceUrl = flow.ServiceUrl,
        ProcessingTokenHash = flow.ProcessingToken is null
            ? null : Hash(flow.ProcessingToken),
        ExternalProviderId = flow.ExternalIdentity?.ProviderId,
        ExternalProtocol = flow.ExternalIdentity?.Protocol,
        ExternalNamespace = flow.ExternalIdentity?.IdentityNamespace,
        ExternalSubject = flow.ExternalIdentity?.Subject,
        ExternalDisplayName = flow.ExternalIdentity?.DisplayName,
        FailureCode = flow.FailureCode
    };

    private static SsoFlowRecord ToRecord(SsoFlowEntity flow) => new(
        flow.Id, flow.ProviderId, flow.Protocol, flow.Intent,
        flow.BrowserIdHash, flow.State, string.Empty,
        flow.ProviderFingerprint, flow.ReturnPath, flow.CreatedAt, flow.ExpiresAt,
        flow.UserId, flow.TokenVersion, flow.Nonce, flow.PkceVerifier,
        flow.ServiceUrl, null,
        flow.ExternalProviderId is Guid providerId
            && flow.ExternalProtocol is { } protocol
            && flow.ExternalNamespace is { } identityNamespace
            && flow.ExternalSubject is { } subject
            ? new SsoExternalIdentity(providerId, protocol, identityNamespace,
                subject, flow.ExternalDisplayName)
            : null,
        flow.FailureCode);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RandomToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

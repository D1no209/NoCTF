using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.API.SignalR.Hubs;

public enum MfaHubKind : short { Competition, Notifications, PlatformLogs, LiveSolo }

public interface IMfaConnectionContextValidator
{
    Task<IReadOnlyDictionary<string, MfaFailure?>> ValidateAsync(IReadOnlyList<MfaContextValidationRequest> requests, CancellationToken ct);
}

public sealed class MfaConnectionGuard(IMfaConnectionContextValidator validator, TimeProvider clock)
{
    private sealed record Connection(HubCallerContext Context, MfaHubKind Kind);
    private readonly ConcurrentDictionary<string, Connection> connections = new();
    public void Register(HubCallerContext context, MfaHubKind kind) => connections[context.ConnectionId] = new(context, kind);
    public void Remove(string id) => connections.TryRemove(id, out _);
    public bool IsRegistered(string id) => connections.ContainsKey(id);
    public async Task<string[]> EligibleAsync(MfaHubKind kind, IReadOnlySet<Guid>? users, CancellationToken ct)
    {
        var candidates = connections.Values.Where(value => value.Kind == kind && (users is null || Guid.TryParse(value.Context.UserIdentifier, out var id) && users.Contains(id))).ToArray();
        return await ValidateAsync(candidates, ct);
    }
    public Task RevalidateAsync(CancellationToken ct) => ValidateAsync(connections.Values.ToArray(), ct);
    private async Task<string[]> ValidateAsync(IReadOnlyList<Connection> candidates, CancellationToken ct)
    {
        var requests = new List<MfaContextValidationRequest>();
        foreach (var candidate in candidates)
        {
            var principal = candidate.Context.User;
            if (principal is null || !Guid.TryParse(candidate.Context.UserIdentifier, out var id) || !int.TryParse(principal.FindFirstValue("token_version"), out var version)
                || !long.TryParse(principal.FindFirstValue("exp"), out var expires) || expires <= clock.GetUtcNow().ToUnixTimeSeconds())
            { candidate.Context.Abort(); Remove(candidate.Context.ConnectionId); continue; }
            requests.Add(new(candidate.Context.ConnectionId, id, version, AuthenticationContextClaims.Read(principal)));
        }
        IReadOnlyDictionary<string, MfaFailure?> decisions;
        try { decisions = await validator.ValidateAsync(requests, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { foreach (var candidate in candidates) { candidate.Context.Abort(); Remove(candidate.Context.ConnectionId); } return []; }
        var valid = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!connections.ContainsKey(candidate.Context.ConnectionId)) continue;
            if (decisions.TryGetValue(candidate.Context.ConnectionId, out var failure) && failure is null) valid.Add(candidate.Context.ConnectionId);
            else { candidate.Context.Abort(); Remove(candidate.Context.ConnectionId); }
        }
        return valid.ToArray();
    }
}

public sealed class MfaHubFilter(MfaConnectionGuard guard) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var kind = context.Hub switch { CompetitionHub => MfaHubKind.Competition, NotificationHub => MfaHubKind.Notifications,
            NoCTF.API.LiveSolo.Realtime.LiveSoloHub => MfaHubKind.LiveSolo, _ => MfaHubKind.PlatformLogs };
        guard.Register(context.Context, kind); await guard.RevalidateAsync(context.Context.ConnectionAborted);
        if (!guard.IsRegistered(context.Context.ConnectionId)) throw new HubException("Authentication is no longer sufficient.");
        await next(context);
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await guard.RevalidateAsync(context.Context.ConnectionAborted);
        if (!guard.IsRegistered(context.Context.ConnectionId)) throw new HubException("Authentication is no longer sufficient.");
        context.Context.ConnectionAborted.ThrowIfCancellationRequested(); return await next(context);
    }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    { guard.Remove(context.Context.ConnectionId); await next(context, exception); }
}

public sealed class MfaConnectionRevalidationAgent(MfaConnectionGuard guard, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await guard.RevalidateAsync(stoppingToken);
    }
}

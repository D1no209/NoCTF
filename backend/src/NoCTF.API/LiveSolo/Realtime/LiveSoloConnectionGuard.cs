using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR.Hubs;
using NoCTF.Application.LiveSolo.Realtime;

namespace NoCTF.API.LiveSolo.Realtime;

public interface ILiveSoloConnectionAccess
{
    Task<IReadOnlySet<string>> EligibleAsync(IReadOnlyList<LiveSoloRealtimeAccessRequest> requests, CancellationToken ct);
}
public sealed class LiveSoloConnectionGuard(ILiveSoloConnectionAccess access, MfaConnectionGuard mfa)
{
    private sealed record Subscription(HubCallerContext Context, LiveSoloRealtimeAccessRequest Request);
    private readonly ConcurrentDictionary<(string Connection, Guid Match), Subscription> subscriptions = new();
    public async Task<bool> JoinAsync(HubCallerContext context, Guid competitionId, Guid matchId, LiveSoloRealtimeAudience audience, CancellationToken ct)
    {
        if (!Guid.TryParse(context.UserIdentifier, out var userId)) return false;
        if (!subscriptions.ContainsKey((context.ConnectionId, matchId)) && subscriptions.Keys.Count(x => x.Connection == context.ConnectionId) >= 16) return false;
        var subscription = new Subscription(context, new(Guid.NewGuid().ToString("N"), userId, competitionId, matchId, audience));
        if (!(await ValidateAsync([subscription], ct)).Contains(context.ConnectionId)) return false;
        subscriptions[(context.ConnectionId, matchId)] = subscription; return true;
    }
    public void Leave(string connectionId, Guid matchId) => subscriptions.TryRemove((connectionId, matchId), out _);
    public void Remove(string connectionId) { foreach (var key in subscriptions.Keys.Where(x => x.Connection == connectionId)) subscriptions.TryRemove(key, out _); }
    public async Task<bool> HeartbeatAsync(string connectionId, Guid matchId, CancellationToken ct)
        => subscriptions.TryGetValue((connectionId, matchId), out var subscription) && (await ValidateAsync([subscription], ct)).Contains(connectionId);
    public Task<string[]> EligibleAsync(Guid competitionId, Guid matchId, CancellationToken ct)
        => ValidateAsync(subscriptions.Values.Where(x => x.Request.CompetitionId == competitionId && x.Request.MatchId == matchId).ToArray(), ct);
    public async Task RevalidateAsync(CancellationToken ct) => await ValidateAsync(subscriptions.Values.ToArray(), ct);
    private async Task<string[]> ValidateAsync(IReadOnlyList<Subscription> candidates, CancellationToken ct)
    {
        try
        {
            var authenticated = (await mfa.EligibleAsync(MfaHubKind.LiveSolo, null, ct)).ToHashSet(StringComparer.Ordinal);
            var eligible = await access.EligibleAsync(candidates.Where(x => authenticated.Contains(x.Context.ConnectionId)).Select(x => x.Request).ToArray(), ct);
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (authenticated.Contains(candidate.Context.ConnectionId) && eligible.Contains(candidate.Request.Key)) result.Add(candidate.Context.ConnectionId);
                else { candidate.Context.Abort(); Remove(candidate.Context.ConnectionId); }
            }
            return result.ToArray();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        { foreach (var candidate in candidates) { candidate.Context.Abort(); Remove(candidate.Context.ConnectionId); } return []; }
    }
}
public sealed class LiveSoloConnectionRevalidationAgent(LiveSoloConnectionGuard guard, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await guard.RevalidateAsync(stoppingToken);
    }
}

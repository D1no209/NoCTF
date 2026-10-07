using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.SignalR.Hubs;

public static class CompetitionHubGroups
{
    public static string Public(Guid competitionId) =>
        $"competition:{competitionId:N}:public";

    public static string Staff(Guid competitionId) =>
        $"competition:{competitionId:N}:staff";
}

public sealed record CompetitionHubSubscription(
    string ConnectionId,
    Guid UserId,
    Guid CompetitionId,
    string GroupName,
    bool IsStaff);

public sealed class CompetitionHubSubscriptionRegistry
{
    private readonly ConcurrentDictionary<string,
        ConcurrentDictionary<Guid, CompetitionHubSubscription>> connections = new();
    private readonly ConcurrentDictionary<Guid,
        ConcurrentDictionary<string, CompetitionHubSubscription>> competitions = new();

    public void Set(CompetitionHubSubscription subscription)
    {
        var connection = connections.GetOrAdd(
            subscription.ConnectionId,
            static _ => new());
        if (connection.TryGetValue(subscription.CompetitionId, out var previous)
            && previous.GroupName != subscription.GroupName
            && competitions.TryGetValue(previous.CompetitionId, out var previousCompetition))
        {
            previousCompetition.TryRemove(previous.ConnectionId, out _);
        }
        connection[subscription.CompetitionId] = subscription;
        competitions.GetOrAdd(subscription.CompetitionId, static _ => new())
            [subscription.ConnectionId] = subscription;
    }

    public bool TryGet(
        string connectionId,
        Guid competitionId,
        out CompetitionHubSubscription subscription)
    {
        if (connections.TryGetValue(connectionId, out var connection)
            && connection.TryGetValue(competitionId, out var found)
            && found is not null)
        {
            subscription = found;
            return true;
        }
        subscription = null!;
        return false;
    }

    public IReadOnlyList<CompetitionHubSubscription> Get(Guid competitionId) =>
        competitions.TryGetValue(competitionId, out var subscriptions)
            ? subscriptions.Values.ToArray()
            : [];

    public void Remove(string connectionId, Guid competitionId)
    {
        if (connections.TryGetValue(connectionId, out var connection))
        {
            connection.TryRemove(competitionId, out _);
            if (connection.IsEmpty)
                connections.TryRemove(connectionId, out _);
        }
        if (competitions.TryGetValue(competitionId, out var competition))
        {
            competition.TryRemove(connectionId, out _);
            if (competition.IsEmpty)
                competitions.TryRemove(competitionId, out _);
        }
    }

    public void RemoveConnection(string connectionId)
    {
        if (!connections.TryRemove(connectionId, out var subscriptions))
            return;
        foreach (var subscription in subscriptions.Values)
        {
            if (!competitions.TryGetValue(subscription.CompetitionId, out var competition))
                continue;
            competition.TryRemove(connectionId, out _);
            if (competition.IsEmpty)
                competitions.TryRemove(subscription.CompetitionId, out _);
        }
    }
}

public interface ICompetitionHubAudienceRouter
{
    Task<ICompetitionHubClient> AllKnownAsync(Guid competitionId, CancellationToken ct);

    Task<ICompetitionHubClient?> CurrentAsync(
        Guid competitionId,
        CancellationToken cancellationToken);
}

public interface ICompetitionHubAudienceAccess
{
    Task<CompetitionAccessMode?> GetAccessModeAsync(
        Guid competitionId,
        CancellationToken cancellationToken);

    Task<CompetitionHubAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken);
}

public sealed class CompetitionHubAudienceRouter(
    ICompetitionHubAudienceAccess access,
    IHubContext<CompetitionHub, ICompetitionHubClient> hub,
    CompetitionHubSubscriptionRegistry subscriptions, MfaConnectionGuard guard)
    : ICompetitionHubAudienceRouter
{
    public async Task<ICompetitionHubClient> AllKnownAsync(Guid competitionId, CancellationToken ct)
    {
        var eligible = await guard.EligibleAsync(MfaHubKind.Competition, null, ct);
        var subscribed = subscriptions.Get(competitionId).Select(value => value.ConnectionId).ToHashSet();
        return hub.Clients.Clients(eligible.Where(subscribed.Contains).ToArray());
    }

    public async Task<ICompetitionHubClient?> CurrentAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var accessMode = await access.GetAccessModeAsync(
            competitionId,
            cancellationToken);
        return accessMode switch
        {
            CompetitionAccessMode.Public => await AllKnownAsync(competitionId, cancellationToken),
            CompetitionAccessMode.StaffOnly =>
                hub.Clients.Clients((await guard.EligibleAsync(MfaHubKind.Competition, null, cancellationToken))
                    .Where(id => subscriptions.Get(competitionId).Any(value => value.ConnectionId == id && value.IsStaff)).ToArray()),
            _ => null
        };
    }
}

public interface ICompetitionHubAudienceCoordinator
{
    Task ApplyAsync(
        CompetitionEventCommitted change,
        CancellationToken cancellationToken);
}

public sealed class CompetitionHubAudienceCoordinator(
    ICompetitionHubAudienceAccess access,
    IHubContext<CompetitionHub, ICompetitionHubClient> hub,
    CompetitionHubSubscriptionRegistry subscriptions)
    : ICompetitionHubAudienceCoordinator
{
    public async Task ApplyAsync(
        CompetitionEventCommitted change,
        CancellationToken cancellationToken)
    {
        var current = subscriptions.Get(change.CompetitionId);
        if (change.AudienceChangeKind == CompetitionAudienceChangeKind.AccessMode
            && change.AccessMode == CompetitionAccessMode.StaffOnly)
        {
            foreach (var subscription in current)
                await ReauthorizeAsync(subscription, cancellationToken);
            return;
        }

        foreach (var subscription in current.Where(item => item.IsStaff))
            await ReauthorizeAsync(subscription, cancellationToken);
    }

    private async Task ReauthorizeAsync(
        CompetitionHubSubscription subscription,
        CancellationToken cancellationToken)
    {
        var decision = await access.ResolveAsync(
            subscription.UserId,
            subscription.CompetitionId,
            cancellationToken);
        if (decision is null)
        {
            await RemoveAsync(subscription, cancellationToken);
            return;
        }

        var groupName = decision.IsStaff
            ? CompetitionHubGroups.Staff(subscription.CompetitionId)
            : CompetitionHubGroups.Public(subscription.CompetitionId);
        if (groupName == subscription.GroupName)
            return;
        await hub.Groups.RemoveFromGroupAsync(
            subscription.ConnectionId,
            subscription.GroupName,
            cancellationToken);
        await hub.Groups.AddToGroupAsync(
            subscription.ConnectionId,
            groupName,
            cancellationToken);
        subscriptions.Set(subscription with
        {
            GroupName = groupName,
            IsStaff = decision.IsStaff
        });
    }

    private async Task RemoveAsync(
        CompetitionHubSubscription subscription,
        CancellationToken cancellationToken)
    {
        await hub.Groups.RemoveFromGroupAsync(
            subscription.ConnectionId,
            subscription.GroupName,
            cancellationToken);
        subscriptions.Remove(subscription.ConnectionId, subscription.CompetitionId);
    }
}

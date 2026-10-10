using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions.Progression;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Progression;

public sealed record ProgressionReconcileResult(
    bool NodeStateChanged,
    int BadgesAwarded,
    int BadgesRevoked);

/// <summary>Updates a team's current graph and personal badge state inside its caller's transaction.</summary>
public sealed class ProgressionReconciler(NoCtfDbContext db)
{
    public async Task ReconcileTeamsAsync(
        Guid competitionId, IReadOnlyList<Guid> teamIds,
        CompetitionProgression graph, DateTimeOffset now, CancellationToken ct)
    {
        if (teamIds.Count == 0) return;
        var ids = teamIds.ToArray();
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(item => ids.Contains(item.Id) && item.CompetitionId == competitionId)
            .Select(item => new
            {
                item.Id,
                Eligible = item.DeletedAt == null && !item.IsBanned
                    && item.RegistrationStatus == TeamRegistrationStatus.Approved
            }).ToDictionaryAsync(item => item.Id, item => item.Eligible, ct);
        var challengeIds = graph.Nodes.OfType<ChallengeProgressionNode>()
            .Select(node => node.CompetitionChallengeId).ToArray();
        var completed = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && ids.Contains(fact.TeamId!.Value)
                && challengeIds.Contains(fact.CompetitionChallengeId)
                && (fact.Kind == GameplayFactKind.FlagAttempt
                    || fact.Kind == GameplayFactKind.FixAttempt)
                && fact.State == GameplayFactState.Completed
                && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
                        && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue))
            .Select(fact => new { fact.TeamId, fact.CompetitionChallengeId })
            .Distinct().ToArrayAsync(ct);
        var nodeStates = await db.TeamProgressionNodeStates
            .Where(state => ids.Contains(state.TeamId)).ToListAsync(ct);
        var badgeStates = await db.TeamProgressionBadgeStates
            .Where(state => ids.Contains(state.TeamId)).ToListAsync(ct);
        var grants = await db.UserBadgeGrants
            .Where(grant => ids.Contains(grant.TeamId)).ToListAsync(ct);
        var members = await db.Set<TeamMember>().AsNoTracking()
            .Where(member => ids.Contains(member.TeamId))
            .Join(db.Users.AsNoTracking(), member => member.UserId,
                user => user.Id, (member, user) => new
                {
                    member.TeamId, user.Id, user.Kind, user.AccountStatus
                })
            .Where(item => item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active)
            .Select(item => new { item.TeamId, item.Id }).ToArrayAsync(ct);
        var completedByTeam = completed.ToLookup(item => item.TeamId,
            item => item.CompetitionChallengeId);
        var nodesByTeam = nodeStates.ToLookup(item => item.TeamId);
        var badgesByTeam = badgeStates.ToLookup(item => item.TeamId);
        var grantsByTeam = grants.ToLookup(item => item.TeamId);
        var membersByTeam = members.ToLookup(item => item.TeamId, item => item.Id);
        foreach (var teamId in ids)
        {
            if (!teams.TryGetValue(teamId, out var eligible))
                throw new InvalidOperationException("Progression team was not found in its competition.");
            await ReconcileTeamCoreAsync(competitionId, teamId, graph, now, null, ct,
                new TeamInputs(eligible, completedByTeam[teamId].ToHashSet(),
                    nodesByTeam[teamId].ToList(), badgesByTeam[teamId].ToList(),
                    grantsByTeam[teamId].ToList(), membersByTeam[teamId].ToArray()));
        }
    }

    public async Task ReconcilePersistedTeamAsync(
        Guid competitionId, Guid teamId, DateTimeOffset now, CancellationToken ct)
    {
        var graph = await db.CompetitionProgressions
            .Include(item => item.Nodes).Include(item => item.Edges)
            .AsSplitQuery().SingleOrDefaultAsync(
                item => item.CompetitionId == competitionId, ct);
        if (graph is null) return;
        await ReconcileTeamAsync(competitionId, teamId, graph, now, null, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProgressionReconcileResult?> ReconcileCompletedFactAsync(
        GameplayFact fact, DateTimeOffset now, CancellationToken ct)
    {
        if (fact.TeamId is not Guid teamId
            || fact.Kind is not (GameplayFactKind.FlagAttempt or GameplayFactKind.FixAttempt))
            return null;
        var graph = await db.CompetitionProgressions
            .Include(item => item.Nodes).Include(item => item.Edges)
            .AsSplitQuery().SingleOrDefaultAsync(
                item => item.CompetitionId == fact.CompetitionId, ct);
        if (graph is not { Enabled: true })
            return null;
        return await ReconcileTeamAsync(fact.CompetitionId, teamId, graph, now, fact, ct);
    }

    public async Task<ProgressionReconcileResult> ReconcileTeamAsync(
        Guid competitionId,
        Guid teamId,
        CompetitionProgression? graph,
        DateTimeOffset now,
        GameplayFact? changingFact,
        CancellationToken ct) => await ReconcileTeamCoreAsync(
            competitionId, teamId, graph, now, changingFact, ct, null);

    private async Task<ProgressionReconcileResult> ReconcileTeamCoreAsync(
        Guid competitionId, Guid teamId, CompetitionProgression? graph,
        DateTimeOffset now, GameplayFact? changingFact, CancellationToken ct,
        TeamInputs? inputs)
    {
        var team = inputs is not null ? null : await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == teamId && item.CompetitionId == competitionId)
            .Select(item => new
            {
                Eligible = item.DeletedAt == null && !item.IsBanned
                    && item.RegistrationStatus == TeamRegistrationStatus.Approved
            })
            .SingleOrDefaultAsync(ct);
        if (team is null && inputs is null)
            throw new InvalidOperationException("Progression team was not found in its competition.");

        var activeGraph = (inputs?.Eligible ?? team!.Eligible)
            && graph is { Enabled: true } ? graph : null;
        ProgressionGraphEvaluation? evaluation = null;
        if (activeGraph is not null)
        {
            var challengeIds = activeGraph.Nodes.OfType<ChallengeProgressionNode>()
                .Select(node => node.CompetitionChallengeId).ToArray();
            var changedId = changingFact?.Id ?? Guid.Empty;
            var completed = inputs?.Completed ?? (await db.GameplayFacts.AsNoTracking()
                    .Where(fact => fact.CompetitionId == competitionId
                        && fact.TeamId == teamId
                        && fact.Id != changedId
                        && challengeIds.Contains(fact.CompetitionChallengeId)
                        && (fact.Kind == GameplayFactKind.FlagAttempt
                            || fact.Kind == GameplayFactKind.FixAttempt)
                        && fact.State == GameplayFactState.Completed
                        && fact.TimeEligibility == NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid
                        && (fact.Result == GameplayFactResult.Correct || fact.Result == GameplayFactResult.RightButDue))
                    .Select(fact => fact.CompetitionChallengeId)
                    .Distinct()
                    .ToArrayAsync(ct))
                .ToHashSet();
            if (changingFact is
                {
                    State: GameplayFactState.Completed,
                    Result: GameplayFactResult.Correct or GameplayFactResult.RightButDue,
                    TimeEligibility: NoCTF.Domain.Challenges.GameplayFactTimeEligibility.Valid,
                    Kind: GameplayFactKind.FlagAttempt or GameplayFactKind.FixAttempt
                } && changingFact.CompetitionId == competitionId
                    && changingFact.TeamId == teamId)
                completed.Add(changingFact.CompetitionChallengeId);
            evaluation = ProgressionGraphRules.Evaluate(
                activeGraph.Nodes, activeGraph.Edges, completed);
        }

        var existingNodes = inputs?.Nodes ?? await db.TeamProgressionNodeStates
            .Where(state => state.TeamId == teamId).ToListAsync(ct);
        var desiredNodes = evaluation?.Nodes
            ?? new Dictionary<Guid, ProgressionNodeState>();
        var nodeChanged = false;
        foreach (var state in existingNodes)
        {
            if (!desiredNodes.TryGetValue(state.NodeId, out var desired))
            {
                db.TeamProgressionNodeStates.Remove(state);
                nodeChanged = true;
                continue;
            }
            if (state.Active != desired.Active || state.Complete != desired.Complete
                || state.GraphRevision != graph!.Revision)
            {
                nodeChanged = true;
                state.Active = desired.Active;
                state.Complete = desired.Complete;
                state.GraphRevision = graph!.Revision;
                state.EvaluatedAt = now;
            }
        }
        var existingNodeIds = existingNodes.Select(state => state.NodeId).ToHashSet();
        foreach (var desired in desiredNodes.Values.Where(state => !existingNodeIds.Contains(state.NodeId)))
        {
            db.TeamProgressionNodeStates.Add(new TeamProgressionNodeState
            {
                TeamId = teamId,
                CompetitionId = competitionId,
                NodeId = desired.NodeId,
                Active = desired.Active,
                Complete = desired.Complete,
                GraphRevision = graph!.Revision,
                EvaluatedAt = now
            });
            nodeChanged = true;
        }

        var currentBadges = inputs?.Badges ?? await db.TeamProgressionBadgeStates
            .Where(state => state.TeamId == teamId).ToListAsync(ct);
        var currentByBadge = currentBadges.ToDictionary(state => state.BadgeId);
        var desiredBadgeIds = evaluation?.ActiveBadgeIds ?? new HashSet<Guid>();
        var badgeIds = currentByBadge.Keys.Concat(desiredBadgeIds).Distinct().ToArray();
        var grants = inputs?.Grants ?? await db.UserBadgeGrants
            .Where(grant => grant.TeamId == teamId && badgeIds.Contains(grant.BadgeId))
            .ToListAsync(ct);
        var grantsByBadge = grants.GroupBy(grant => grant.BadgeId)
            .ToDictionary(group => group.Key, group => group.ToList());
        Guid[]? eligibleMemberIds = null;
        var awarded = 0;
        var revoked = 0;
        foreach (var badgeId in badgeIds)
        {
            var desiredActive = desiredBadgeIds.Contains(badgeId);
            currentByBadge.TryGetValue(badgeId, out var state);
            var wasActive = state?.Active == true;
            if (state is null)
            {
                state = new TeamProgressionBadgeState
                {
                    TeamId = teamId,
                    CompetitionId = competitionId,
                    BadgeId = badgeId,
                    Active = desiredActive,
                    GraphRevision = graph?.Revision ?? 0,
                    EvaluatedAt = now
                };
                db.TeamProgressionBadgeStates.Add(state);
            }
            else
            {
                if (state.Active != desiredActive
                    || state.GraphRevision != (graph?.Revision ?? 0))
                {
                    state.Active = desiredActive;
                    state.GraphRevision = graph?.Revision ?? 0;
                    state.EvaluatedAt = now;
                }
            }

            if (wasActive == desiredActive && !(grantsByBadge.GetValueOrDefault(badgeId)
                    ?.Any(grant => grant.Active && !desiredActive) ?? false))
                continue;
            var badgeGrants = grantsByBadge.GetValueOrDefault(badgeId) ?? [];
            if (desiredActive)
            {
                eligibleMemberIds ??= inputs?.EligibleMembers ?? await db.Set<TeamMember>().AsNoTracking()
                    .Where(member => member.TeamId == teamId)
                    .Join(db.Users.AsNoTracking(), member => member.UserId, user => user.Id,
                        (_, user) => user)
                    .Where(user => user.Kind == UserKind.Human
                        && user.AccountStatus == UserAccountStatus.Active)
                    .Select(user => user.Id)
                    .ToArrayAsync(ct);
                foreach (var userId in eligibleMemberIds)
                {
                    var grant = badgeGrants.SingleOrDefault(item => item.UserId == userId);
                    if (grant?.Active == true) continue;
                    if (grant is null)
                    {
                        grant = new UserBadgeGrant
                        {
                            TeamId = teamId,
                            CompetitionId = competitionId,
                            BadgeId = badgeId,
                            UserId = userId
                        };
                        db.UserBadgeGrants.Add(grant);
                    }
                    grant.Active = true;
                    grant.AwardedAt = now;
                    grant.RevokedAt = null;
                    AddTransition(grant, BadgeTransitionKind.Awarded, graph!.Revision, now);
                    awarded++;
                }
            }
            else
            {
                foreach (var grant in badgeGrants.Where(item => item.Active))
                {
                    grant.Active = false;
                    grant.RevokedAt = now;
                    AddTransition(grant, BadgeTransitionKind.Revoked, graph?.Revision ?? 0, now);
                    revoked++;
                }
            }
        }
        return new(nodeChanged, awarded, revoked);
    }

    private sealed record TeamInputs(
        bool Eligible,
        HashSet<Guid> Completed,
        List<TeamProgressionNodeState> Nodes,
        List<TeamProgressionBadgeState> Badges,
        List<UserBadgeGrant> Grants,
        Guid[] EligibleMembers);

    private void AddTransition(
        UserBadgeGrant grant,
        BadgeTransitionKind kind,
        long revision,
        DateTimeOffset now) => db.UserBadgeTransitions.Add(new UserBadgeTransition
    {
        Id = Guid.CreateVersion7(now),
        CompetitionId = grant.CompetitionId,
        TeamId = grant.TeamId,
        BadgeId = grant.BadgeId,
        UserId = grant.UserId,
        Kind = kind,
        GraphRevision = revision,
        OccurredAt = now
    });
}

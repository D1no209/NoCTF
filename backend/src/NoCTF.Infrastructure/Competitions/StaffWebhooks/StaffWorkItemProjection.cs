using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

internal static class StaffWorkItemProjection
{
    internal sealed record Scope(Guid[] IncidentIds, Guid[] ConsultationIds);
    internal sealed record Event(Guid Id, CompetitionEventKind Kind, Guid? FactId, Guid? ParentId,
        Guid? TeamId, Guid? ActorId, DateTimeOffset At);
    internal sealed record Node(Guid Id, Guid? RootId, NotificationKind Kind, Guid? TeamId, Guid? ChallengeId,
        CompetitionQuestionSubject? Subject, CompetitionQuestionStatus? Status,
        CompetitionQuestionParticipantRole? Role, Guid? ActorId, DateTimeOffset At);
    internal sealed record Fact(Guid Id, Guid? TeamId, Guid? RelatedTeamId, Guid ChallengeId,
        GameplayFactFailureCode? Failure, DateTimeOffset At, DateTimeOffset UpdatedAt);
    private static readonly CompetitionEventKind[] RelevantEvents = [CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed, CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected, CompetitionEventKind.TeamBanned,
        CompetitionEventKind.TeamBanAppealSubmitted, CompetitionEventKind.TeamBanAppealAccepted, CompetitionEventKind.TeamBanAppealUpheld];

    public static async Task<IReadOnlyList<StaffWorkItemSummary>> ReadAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct, Scope? scope = null)
    {
        var factQuery = db.GameplayFacts.AsNoTracking().Where(value => value.CompetitionId == competitionId
            && value.CompetitionChallengeId != null && value.FailureCode != null && CheatIncidentFailures.All.Contains(value.FailureCode.Value));
        if (scope is not null) factQuery = factQuery.Where(value => scope.IncidentIds.Contains(value.Id));
        var facts = (scope is { IncidentIds.Length: 0 } ? [] : await factQuery
            .Select(value => new Fact(value.Id, value.TeamId, value.VictimTeamId, value.CompetitionChallengeId!.Value,
                value.FailureCode, value.OccurredAt, value.UpdatedAt)).ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var entry in db.ChangeTracker.Entries<GameplayFact>().Where(value => value.Entity.CompetitionId == competitionId
                     && (scope is null || scope.IncidentIds.Contains(value.Entity.Id))
                     && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var value = entry.Entity;
            if (value.CompetitionChallengeId is null || entry.State == EntityState.Deleted || !CheatIncidentFailures.IsIncident(value.FailureCode)) facts.Remove(value.Id);
            else facts[value.Id] = new(value.Id, value.TeamId, value.VictimTeamId, value.CompetitionChallengeId!.Value, value.FailureCode, value.OccurredAt, value.UpdatedAt);
        }
        var eventQuery = db.CompetitionEvents.AsNoTracking().Where(value => value.CompetitionId == competitionId && RelevantEvents.Contains(value.Kind));
        if (scope is not null) eventQuery = eventQuery.Where(value => value.GameplayFactId != null && scope.IncidentIds.Contains(value.GameplayFactId.Value)
            && (value.Kind == CompetitionEventKind.CheatIncidentConfirmed || value.Kind == CompetitionEventKind.CheatIncidentDismissed
                || value.Kind == CompetitionEventKind.CheatIncidentSuperseded || value.Kind == CompetitionEventKind.CheatIncidentCorrected));
        var events = (scope is { IncidentIds.Length: 0 } ? [] : await eventQuery
            .Select(value => new Event(value.Id, value.Kind, value.GameplayFactId, value.ParentEventId, value.TeamId, value.ActorUserId, value.OccurredAt))
            .ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var value in db.ChangeTracker.Entries<CompetitionEvent>().Where(entry => entry.State == EntityState.Added
                     && entry.Entity.CompetitionId == competitionId && RelevantEvents.Contains(entry.Entity.Kind)
                     && (scope is null || entry.Entity.GameplayFactId is { } factId && scope.IncidentIds.Contains(factId)
                         && entry.Entity.Kind is CompetitionEventKind.CheatIncidentConfirmed or CompetitionEventKind.CheatIncidentDismissed
                             or CompetitionEventKind.CheatIncidentSuperseded or CompetitionEventKind.CheatIncidentCorrected)).Select(entry => entry.Entity))
            events[value.Id] = new(value.Id, value.Kind, value.GameplayFactId, value.ParentEventId, value.TeamId, value.ActorUserId, value.OccurredAt);
        var nodeQuery = db.Notifications.AsNoTracking().Where(value => value.TargetType == NotificationTargetType.CompetitionCollaborators && value.TargetId == competitionId
                && (value.Kind == NotificationKind.QuestionOpened || value.Kind == NotificationKind.QuestionStatusChanged
                    || value.Kind == NotificationKind.Message && value.ThreadRootId != null));
        if (scope is not null) nodeQuery = nodeQuery.Where(value => scope.ConsultationIds.Contains(value.Id)
            || value.ThreadRootId != null && scope.ConsultationIds.Contains(value.ThreadRootId.Value));
        var nodes = (scope is { ConsultationIds.Length: 0 } ? [] : await nodeQuery
            .Select(value => new Node(value.Id, value.ThreadRootId, value.Kind, value.TeamId, value.CompetitionChallengeId,
                value.QuestionSubject, value.QuestionStatus, value.QuestionActorRole, value.ActorUserId ?? value.SourceId, value.SentAt))
            .ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var value in db.ChangeTracker.Entries<Notification>().Where(entry => entry.State == EntityState.Added
                     && entry.Entity.TargetType == NotificationTargetType.CompetitionCollaborators
                     && (scope is null || scope.ConsultationIds.Contains(entry.Entity.ThreadRootId ?? entry.Entity.Id))
                     && entry.Entity.TargetId == competitionId && (entry.Entity.Kind == NotificationKind.QuestionOpened
                         || entry.Entity.ThreadRootId != null && entry.Entity.QuestionStatus != null)).Select(entry => entry.Entity))
            nodes[value.Id] = new(value.Id, value.ThreadRootId, value.Kind, value.TeamId, value.CompetitionChallengeId,
                value.QuestionSubject, value.QuestionStatus, value.QuestionActorRole, value.ActorUserId ?? value.SourceId, value.SentAt);
        var teamIds = facts.Values.Select(value => value.TeamId).Concat(facts.Values.Select(value => value.RelatedTeamId))
            .Concat(nodes.Values.Select(value => value.TeamId)).Concat(events.Values.Select(value => value.TeamId)).OfType<Guid>().Distinct().ToArray();
        var teamQuery = db.Teams.IgnoreQueryFilters().AsNoTracking().Where(value => value.CompetitionId == competitionId);
        if (scope is not null) teamQuery = teamQuery.Where(value => teamIds.Contains(value.Id));
        var teams = scope is not null && teamIds.Length == 0 ? [] : await teamQuery
            .Select(value => new { value.Id, value.Name, value.IsBanned }).ToDictionaryAsync(value => value.Id, ct);
        foreach (var team in db.Teams.Local.Where(value => value.CompetitionId == competitionId && (scope is null || teamIds.Contains(value.Id))))
            teams[team.Id] = new { team.Id, team.Name, team.IsBanned };
        var challengeIds = facts.Values.Select(value => (Guid?)value.ChallengeId).Concat(nodes.Values.Select(value => value.ChallengeId)).OfType<Guid>().Distinct().ToArray();
        var challengeQuery = db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().Where(value => value.CompetitionId == competitionId);
        if (scope is not null) challengeQuery = challengeQuery.Where(value => challengeIds.Contains(value.Id));
        var challenges = scope is not null && challengeIds.Length == 0 ? [] : await challengeQuery
            .Join(db.Challenges.IgnoreQueryFilters(), value => value.ChallengeId, template => template.Id,
                (value, template) => new { value.Id, template.Title, template.Direction }).ToDictionaryAsync(value => value.Id, ct);
        var instanceQuery = db.CompetitionChallenges.AsNoTracking().Where(value => value.CompetitionId == competitionId);
        if (scope is not null) instanceQuery = instanceQuery.Where(value => challengeIds.Contains(value.Id));
        var instances = scope is not null && challengeIds.Length == 0 || !db.Challenges.Local.Any() ? [] : await instanceQuery
            .Select(value => new { value.Id, value.ChallengeId }).ToArrayAsync(ct);
        foreach (var template in db.Challenges.Local)
            foreach (var instance in instances.Where(value => value.ChallengeId == template.Id))
                challenges[instance.Id] = new { instance.Id, template.Title, template.Direction };
        var actorIds = events.Values.Select(value => value.ActorId).Concat(nodes.Values.Select(value => value.ActorId)).OfType<Guid>().Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(value => actorIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.UserName, ct);
        var result = new List<StaffWorkItemSummary>();
        foreach (var fact in facts.Values)
        {
            var resolution = events.Values.Where(value => value.FactId == fact.Id && value.Kind is CompetitionEventKind.CheatIncidentConfirmed
                or CompetitionEventKind.CheatIncidentDismissed or CompetitionEventKind.CheatIncidentSuperseded or CompetitionEventKind.CheatIncidentCorrected)
                .OrderByDescending(value => value.At).ThenByDescending(value => value.Id).FirstOrDefault();
            var status = resolution?.Kind switch { CompetitionEventKind.CheatIncidentConfirmed => CheatIncidentStatus.Confirmed,
                CompetitionEventKind.CheatIncidentDismissed => CheatIncidentStatus.Dismissed, CompetitionEventKind.CheatIncidentSuperseded => CheatIncidentStatus.Superseded,
                CompetitionEventKind.CheatIncidentCorrected => CheatIncidentStatus.Corrected, _ => CheatIncidentStatus.Pending };
            result.Add(Enrich(new() { Kind = StaffWorkItemKind.CheatIncident, Id = fact.Id, CheatStatus = status,
                RequiresStaffAction = status == CheatIncidentStatus.Pending, ActionRequiredSince = status == CheatIncidentStatus.Pending ? fact.UpdatedAt : null,
                CreatedAt = fact.At, DetectedAt = fact.UpdatedAt, UpdatedAt = resolution?.At ?? fact.UpdatedAt,
                TeamId = fact.TeamId, RelatedTeamId = fact.RelatedTeamId, ChallengeId = fact.ChallengeId, ReasonCode = fact.Failure }, resolution?.ActorId));
        }
        foreach (var root in nodes.Values.Where(value => value.Kind == NotificationKind.QuestionOpened && value.RootId == null))
        {
            var history = nodes.Values.Where(value => value.RootId == root.Id).Append(root).OrderBy(value => value.At).ThenBy(value => value.Id).ToArray();
            var last = history[^1];
            var status = last.Status ?? root.Status ?? CompetitionQuestionStatus.Pending;
            var lastMessage = history.LastOrDefault(value => value.Kind is NotificationKind.Message or NotificationKind.QuestionOpened)!;
            var needsAction = status == CompetitionQuestionStatus.Pending && (lastMessage.Role is null
                || CompetitionQuestionRules.IsParticipant(lastMessage.Role.Value));
            // Find the start of this uninterrupted wait; subsequent participant messages do not restart it.
            DateTimeOffset? since = null;
            foreach (var node in history)
            {
                var waiting = node.Status == CompetitionQuestionStatus.Pending && (node.Kind == NotificationKind.QuestionStatusChanged
                    ? since is not null : node.Role is null || CompetitionQuestionRules.IsParticipant(node.Role.Value));
                since = waiting ? since ?? node.At : null;
            }
            result.Add(Enrich(new() { Kind = StaffWorkItemKind.Consultation, Id = root.Id, ConsultationStatus = status,
                RequiresStaffAction = needsAction, ActionRequiredSince = needsAction ? since ?? lastMessage.At : null,
                CreatedAt = root.At, UpdatedAt = last.At, TeamId = root.TeamId, ChallengeId = root.ChallengeId, Subject = root.Subject }, last.ActorId));
        }
        foreach (var appeal in events.Values.Where(value => value.Kind == CompetitionEventKind.TeamBanAppealSubmitted))
        {
            var resolution = events.Values.Where(value => value.ParentId == appeal.Id && value.Kind is CompetitionEventKind.TeamBanAppealAccepted
                or CompetitionEventKind.TeamBanAppealUpheld).OrderByDescending(value => value.At).ThenByDescending(value => value.Id).FirstOrDefault();
            var status = resolution?.Kind switch { CompetitionEventKind.TeamBanAppealAccepted => TeamBanAppealStatus.Accepted,
                CompetitionEventKind.TeamBanAppealUpheld => TeamBanAppealStatus.Upheld, _ => TeamBanAppealStatus.Submitted };
            var currentBan = events.Values.Where(value => value.Kind == CompetitionEventKind.TeamBanned && value.TeamId == appeal.TeamId)
                .OrderByDescending(value => value.At).ThenByDescending(value => value.Id).FirstOrDefault();
            var banned = appeal.TeamId is { } teamId && teams.TryGetValue(teamId, out var team) && team.IsBanned;
            var trackedTeam = db.ChangeTracker.Entries<Team>().FirstOrDefault(value => value.Entity.Id == appeal.TeamId);
            if (trackedTeam is not null) banned = trackedTeam.Entity.IsBanned;
            var required = status == TeamBanAppealStatus.Submitted && banned && currentBan?.Id == appeal.ParentId;
            result.Add(Enrich(new() { Kind = StaffWorkItemKind.BanAppeal, Id = appeal.Id, AppealStatus = status,
                RequiresStaffAction = required, ActionRequiredSince = required ? appeal.At : null,
                CreatedAt = appeal.At, UpdatedAt = resolution?.At ?? appeal.At, TeamId = appeal.TeamId }, resolution?.ActorId ?? appeal.ActorId));
        }
        return result;

        StaffWorkItemSummary Enrich(StaffWorkItemSummary item, Guid? actorId)
        {
            if (item.TeamId is { } teamId && teams.TryGetValue(teamId, out var team)) item.TeamName = team.Name;
            if (item.RelatedTeamId is { } relatedId && teams.TryGetValue(relatedId, out var related)) item.RelatedTeamName = related.Name;
            if (item.ChallengeId is { } challengeId && challenges.TryGetValue(challengeId, out var challenge))
            { item.ChallengeTitle = challenge.Title; item.Direction = challenge.Direction; }
            if (actorId is { } id && names.TryGetValue(id, out var name)) item.ActorDisplayName = name;
            return item;
        }
    }
}

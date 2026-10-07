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

    public static async Task<IReadOnlyList<StaffWorkItemSummary>> ReadAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct)
    {
        var facts = (await db.GameplayFacts.AsNoTracking().Where(value => value.CompetitionId == competitionId
            && value.FailureCode != null && CheatIncidentFailures.All.Contains(value.FailureCode.Value))
            .Select(value => new Fact(value.Id, value.TeamId, value.VictimTeamId, value.CompetitionChallengeId,
                value.FailureCode, value.OccurredAt, value.UpdatedAt)).ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var entry in db.ChangeTracker.Entries<GameplayFact>().Where(value => value.Entity.CompetitionId == competitionId
                     && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var value = entry.Entity;
            if (entry.State == EntityState.Deleted || !CheatIncidentFailures.IsIncident(value.FailureCode)) facts.Remove(value.Id);
            else facts[value.Id] = new(value.Id, value.TeamId, value.VictimTeamId, value.CompetitionChallengeId, value.FailureCode, value.OccurredAt, value.UpdatedAt);
        }
        var events = (await db.CompetitionEvents.AsNoTracking().Where(value => value.CompetitionId == competitionId && RelevantEvents.Contains(value.Kind))
            .Select(value => new Event(value.Id, value.Kind, value.GameplayFactId, value.ParentEventId, value.TeamId, value.ActorUserId, value.OccurredAt))
            .ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var value in db.ChangeTracker.Entries<CompetitionEvent>().Where(entry => entry.State == EntityState.Added
                     && entry.Entity.CompetitionId == competitionId && RelevantEvents.Contains(entry.Entity.Kind)).Select(entry => entry.Entity))
            events[value.Id] = new(value.Id, value.Kind, value.GameplayFactId, value.ParentEventId, value.TeamId, value.ActorUserId, value.OccurredAt);
        var nodes = (await db.Notifications.AsNoTracking().Where(value => value.TargetType == NotificationTargetType.CompetitionCollaborators && value.TargetId == competitionId
                && (value.Kind == NotificationKind.QuestionOpened || value.Kind == NotificationKind.QuestionStatusChanged
                    || value.Kind == NotificationKind.Message && value.ThreadRootId != null))
            .Select(value => new Node(value.Id, value.ThreadRootId, value.Kind, value.TeamId, value.CompetitionChallengeId,
                value.QuestionSubject, value.QuestionStatus, value.QuestionActorRole, value.ActorUserId ?? value.SourceId, value.SentAt))
            .ToArrayAsync(ct)).ToDictionary(value => value.Id);
        foreach (var value in db.ChangeTracker.Entries<Notification>().Where(entry => entry.State == EntityState.Added
                     && entry.Entity.TargetType == NotificationTargetType.CompetitionCollaborators
                     && entry.Entity.TargetId == competitionId && (entry.Entity.Kind == NotificationKind.QuestionOpened
                         || entry.Entity.ThreadRootId != null && entry.Entity.QuestionStatus != null)).Select(entry => entry.Entity))
            nodes[value.Id] = new(value.Id, value.ThreadRootId, value.Kind, value.TeamId, value.CompetitionChallengeId,
                value.QuestionSubject, value.QuestionStatus, value.QuestionActorRole, value.ActorUserId ?? value.SourceId, value.SentAt);
        var teams = await db.Teams.IgnoreQueryFilters().AsNoTracking().Where(value => value.CompetitionId == competitionId)
            .Select(value => new { value.Id, value.Name, value.IsBanned }).ToDictionaryAsync(value => value.Id, ct);
        foreach (var team in db.Teams.Local.Where(value => value.CompetitionId == competitionId))
            teams[team.Id] = new { team.Id, team.Name, team.IsBanned };
        var challenges = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().Where(value => value.CompetitionId == competitionId)
            .Join(db.Challenges.IgnoreQueryFilters(), value => value.ChallengeId, template => template.Id,
                (value, template) => new { value.Id, template.Title, template.Direction }).ToDictionaryAsync(value => value.Id, ct);
        var instances = await db.CompetitionChallenges.AsNoTracking().Where(value => value.CompetitionId == competitionId)
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

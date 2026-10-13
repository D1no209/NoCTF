using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions.StaffWebhooks;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.StaffWebhooks;

/// <summary>Captures allowlisted staff projections in the same transaction as the source mutation.</summary>
internal static class StaffWebhookEventCapture
{
    private static readonly CompetitionEventKind[] Kinds = [CompetitionEventKind.CheatIncidentDetected,
        CompetitionEventKind.CheatIncidentConfirmed, CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded, CompetitionEventKind.CheatIncidentCorrected,
        CompetitionEventKind.TeamBanAppealSubmitted, CompetitionEventKind.TeamBanAppealAccepted, CompetitionEventKind.TeamBanAppealUpheld,
        CompetitionEventKind.TeamBanned, CompetitionEventKind.TeamUnbanned, CompetitionEventKind.TeamBanCorrectionPublished,
        CompetitionEventKind.QuestionOpened, CompetitionEventKind.QuestionReplied, CompetitionEventKind.QuestionStatusChanged,
        CompetitionEventKind.TeamUpdated, CompetitionEventKind.ChallengeUpdated, CompetitionEventKind.ChallengeDescriptionUpdated, CompetitionEventKind.CompetitionUpdated];
    public static Guid[] ChangedCompetitions(NoCtfDbContext db) => db.ChangeTracker.Entries<CompetitionEvent>()
        .Where(value => value.State == EntityState.Added && Kinds.Contains(value.Entity.Kind)).Select(value => value.Entity.CompetitionId)
        .Concat(db.ChangeTracker.Entries<GameplayFact>().Where(value => value.State is EntityState.Added or EntityState.Modified
            && (CheatIncidentFailures.IsIncident(value.Entity.FailureCode) || CheatIncidentFailures.IsIncident(value.OriginalValues.GetValue<GameplayFactFailureCode?>(nameof(GameplayFact.FailureCode)))))
            .Select(value => value.Entity.CompetitionId))
        .Concat(db.ChangeTracker.Entries<Notification>().Where(value => value.State == EntityState.Added
            && value.Entity.QuestionStatus != null && value.Entity.TargetType == NotificationTargetType.CompetitionCollaborators)
            .Select(value => value.Entity.TargetId))
        .Distinct().ToArray();

    public static async Task CaptureAsync(NoCtfDbContext db, Guid[] competitions, DateTimeOffset now, CancellationToken ct, bool force = false)
    {
        foreach (var id in competitions)
        {
            // Unsubscribed competitions do not build an unnecessary read model on every business write.
            // Initial/re-enabled subscriptions rebuild their baseline from authoritative facts.
            if (!force && !db.StaffWebhookTargets.Local.Any(value => value.CompetitionId == id && value.Enabled && !value.Deleted)
                && !await db.StaffWebhookTargets.AnyAsync(value => value.CompetitionId == id && value.Enabled && !value.Deleted, ct)) continue;
            var scope = force ? null : ChangedScope(db, id);
            var stream = scope is null ? null : await FindStreamAsync(db, id, ct);
            // A first capture must establish the complete baseline, including older work items.
            if (stream is null || db.Entry(stream).State == EntityState.Added) scope = null;
            // Global catalog edits and bulk rejudges can commit without tracked source changes.
            // Keep the full capture's catch-up without loading every item's history.
            if (scope is not null && await NeedsFullCatchUpAsync(db, id, ct)) scope = null;
            var current = await StaffWorkItemProjection.ReadAsync(db, id, ct, scope);
            var existingQuery = db.StaffWebhookWorkItems.Where(value => value.CompetitionId == id);
            if (scope is not null) existingQuery = existingQuery.Where(value =>
                value.Kind == StaffWorkItemKind.CheatIncident && scope.IncidentIds.Contains(value.ItemId)
                || value.Kind == StaffWorkItemKind.Consultation && scope.ConsultationIds.Contains(value.ItemId));
            var existing = await existingQuery.ToDictionaryAsync(value => (value.Kind, value.ItemId), ct);
            if (current.Count == 0 && existing.Count == 0) continue;
            stream ??= await StreamAsync(db, id, ct);
            var title = db.Competitions.Local.FirstOrDefault(value => value.Id == id)?.Title
                ?? await db.Competitions.IgnoreQueryFilters().Where(value => value.Id == id).Select(value => value.Title).SingleAsync(ct);
            var changedNodes = db.ChangeTracker.Entries<Notification>().Where(value => value.State == EntityState.Added
                    && value.Entity.TargetType == NotificationTargetType.CompetitionCollaborators && value.Entity.TargetId == id)
                .Select(value => value.Entity).ToArray();
            foreach (var item in current.OrderBy(value => value.Kind).ThenBy(value => value.Id))
            {
                existing.TryGetValue((item.Kind, item.Id), out var old);
                if (old is not null && Same(old.Summary, item)) continue;
                if (db.ChangeTracker.Entries<StaffWebhookEvent>().Any(value => value.State == EntityState.Added
                    && value.Entity.CompetitionId == id && value.Entity.Items.Any(child => child.Summary.Kind == item.Kind && child.Summary.Id == item.Id))) continue;
                var node = changedNodes.LastOrDefault(value => (value.ThreadRootId ?? value.Id) == item.Id);
                var change = old is null ? StaffWorkItemChangeKind.Created : item.Kind == StaffWorkItemKind.Consultation && node?.Kind == NotificationKind.Message
                    ? node.QuestionActorRole is NoCTF.Domain.Challenges.Questions.CompetitionQuestionParticipantRole.Asker or NoCTF.Domain.Challenges.Questions.CompetitionQuestionParticipantRole.Participant
                        ? StaffWorkItemChangeKind.ParticipantMessage : StaffWorkItemChangeKind.StaffReply
                    : old.Summary.CheatStatus != item.CheatStatus || old.Summary.ConsultationStatus != item.ConsultationStatus
                        || old.Summary.AppealStatus != item.AppealStatus || old.Summary.RequiresStaffAction != item.RequiresStaffAction
                        ? StaffWorkItemChangeKind.StatusChanged : StaffWorkItemChangeKind.MetadataChanged;
                if (item.Kind == StaffWorkItemKind.CheatIncident && item.RequiresStaffAction && old?.Summary.RequiresStaffAction == true)
                    item.ActionRequiredSince = old.Summary.ActionRequiredSince;
                var sequence = checked(++stream.Sequence); stream.LatestBusinessSequence = sequence; item.LastChangedSequence = sequence;
                var row = old ?? new StaffWebhookWorkItem { CompetitionId = id, Kind = item.Kind, ItemId = item.Id };
                row.Summary = item.Copy(); if (old is null) db.StaffWebhookWorkItems.Add(row);
                AddEvent(db, stream, change == StaffWorkItemChangeKind.Created ? StaffWebhookEventKind.WorkItemCreated : StaffWebhookEventKind.WorkItemUpdated,
                    now, change, [item.Copy()], sequence: sequence).CompetitionTitle = title;
            }
            var keys = current.Select(value => (value.Kind, value.Id)).ToHashSet();
            foreach (var old in existing.Values.Where(value => !keys.Contains((value.Kind, value.ItemId)) && value.Summary.RequiresStaffAction))
            {
                old.Summary.RequiresStaffAction = false; old.Summary.ActionRequiredSince = null; old.Summary.UpdatedAt = now;
                if (old.Kind == StaffWorkItemKind.CheatIncident) old.Summary.CheatStatus = CheatIncidentStatus.Superseded;
                old.Summary.LastChangedSequence = checked(++stream.Sequence); stream.LatestBusinessSequence = stream.Sequence;
                AddEvent(db, stream, StaffWebhookEventKind.WorkItemUpdated, now, StaffWorkItemChangeKind.StatusChanged, [old.Summary.Copy()], sequence: stream.Sequence).CompetitionTitle = title;
            }
        }
    }
    private static Task<bool> NeedsFullCatchUpAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct)
    {
        var teams = db.Teams.IgnoreQueryFilters().Where(value => value.CompetitionId == competitionId);
        var challenges = db.CompetitionChallenges.IgnoreQueryFilters().Where(value => value.CompetitionId == competitionId)
            .Join(db.Challenges.IgnoreQueryFilters(), value => value.ChallengeId, template => template.Id,
                (value, template) => new { value.Id, template.Title, template.Direction });
        var incidents = db.GameplayFacts.Where(value => value.CompetitionId == competitionId && value.CompetitionChallengeId != null
            && value.FailureCode != null && CheatIncidentFailures.All.Contains(value.FailureCode.Value));
        var questions = db.Notifications.Where(value => value.TargetType == NotificationTargetType.CompetitionCollaborators
            && value.TargetId == competitionId && value.Kind == NotificationKind.QuestionOpened && value.ThreadRootId == null);
        var appeals = db.CompetitionEvents.Where(value => value.CompetitionId == competitionId && value.Kind == CompetitionEventKind.TeamBanAppealSubmitted);
        var items = db.StaffWebhookWorkItems.AsNoTracking().Where(value => value.CompetitionId == competitionId)
            .Select(item => new
            {
                Item = item,
                HasCurrent = item.Kind == StaffWorkItemKind.CheatIncident && incidents.Any(fact => fact.Id == item.ItemId)
                    || item.Kind == StaffWorkItemKind.Consultation && questions.Any(question => question.Id == item.ItemId)
                    || item.Kind == StaffWorkItemKind.BanAppeal && appeals.Any(appeal => appeal.Id == item.ItemId)
            });
        // Closed tombstones retain their historical enrichment; the full projection cannot refresh it.
        return items.AnyAsync(value => value.HasCurrent && (
                value.Item.Summary.TeamName != teams.Where(team => team.Id == value.Item.Summary.TeamId).Select(team => team.Name).FirstOrDefault()
                || value.Item.Summary.RelatedTeamName != teams.Where(team => team.Id == value.Item.Summary.RelatedTeamId).Select(team => team.Name).FirstOrDefault()
                || value.Item.Summary.ChallengeTitle != challenges.Where(challenge => challenge.Id == value.Item.Summary.ChallengeId).Select(challenge => challenge.Title).FirstOrDefault()
                || value.Item.Summary.Direction != challenges.Where(challenge => challenge.Id == value.Item.Summary.ChallengeId).Select(challenge => challenge.Direction).FirstOrDefault())
            || value.Item.Summary.RequiresStaffAction && !value.HasCurrent
            || value.Item.Kind == StaffWorkItemKind.CheatIncident && incidents.Any(fact => fact.Id == value.Item.ItemId
                && (fact.FailureCode != value.Item.Summary.ReasonCode
                    || value.Item.Summary.CheatStatus == CheatIncidentStatus.Pending && fact.UpdatedAt != value.Item.Summary.UpdatedAt)), ct);
    }
    private static StaffWorkItemProjection.Scope? ChangedScope(NoCtfDbContext db, Guid competitionId)
    {
        // Metadata and ban/appeal operations can change several independent work items.
        // Their full projection also preserves all pending tracked metadata overlays.
        if (db.ChangeTracker.Entries<Competition>().Any(value => value.Entity.Id == competitionId && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || db.ChangeTracker.Entries<Team>().Any(value => value.Entity.CompetitionId == competitionId && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || db.ChangeTracker.Entries<CompetitionChallenge>().Any(value => value.Entity.CompetitionId == competitionId && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || db.ChangeTracker.Entries<Challenge>().Any(value => value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || db.ChangeTracker.Entries<StaffWebhookTarget>().Any(value => value.Entity.CompetitionId == competitionId && value.State is EntityState.Added or EntityState.Modified))
            return null;
        var incidents = new HashSet<Guid>();
        var consultations = new HashSet<Guid>();
        foreach (var entry in db.ChangeTracker.Entries<CompetitionEvent>().Where(value => value.State == EntityState.Added
                     && value.Entity.CompetitionId == competitionId && Kinds.Contains(value.Entity.Kind)))
        {
            var change = entry.Entity;
            switch (change.Kind)
            {
                case CompetitionEventKind.CheatIncidentDetected:
                case CompetitionEventKind.CheatIncidentConfirmed:
                case CompetitionEventKind.CheatIncidentDismissed:
                case CompetitionEventKind.CheatIncidentSuperseded:
                case CompetitionEventKind.CheatIncidentCorrected:
                    if (change.GameplayFactId is not { } factId) return null;
                    incidents.Add(factId);
                    break;
                case CompetitionEventKind.QuestionOpened:
                case CompetitionEventKind.QuestionReplied:
                case CompetitionEventKind.QuestionStatusChanged:
                    if (change.QuestionId is not { } questionId) return null;
                    consultations.Add(questionId);
                    break;
                default:
                    return null;
            }
        }
        foreach (var entry in db.ChangeTracker.Entries<GameplayFact>().Where(value => value.Entity.CompetitionId == competitionId
                     && value.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            incidents.Add(entry.Entity.Id);
        foreach (var entry in db.ChangeTracker.Entries<Notification>().Where(value => value.State == EntityState.Added
                     && value.Entity.TargetType == NotificationTargetType.CompetitionCollaborators && value.Entity.TargetId == competitionId
                     && (value.Entity.Kind == NotificationKind.QuestionOpened || value.Entity.ThreadRootId != null && value.Entity.QuestionStatus != null)))
            consultations.Add(entry.Entity.ThreadRootId ?? entry.Entity.Id);
        return incidents.Count == 0 && consultations.Count == 0 ? null : new(incidents.ToArray(), consultations.ToArray());
    }
    private static bool Same(StaffWorkItemSummary left, StaffWorkItemSummary right) => left.CheatStatus == right.CheatStatus
        && left.ConsultationStatus == right.ConsultationStatus && left.AppealStatus == right.AppealStatus
        && left.RequiresStaffAction == right.RequiresStaffAction && left.UpdatedAt == right.UpdatedAt
        && left.TeamName == right.TeamName && left.RelatedTeamName == right.RelatedTeamName
        && left.ChallengeTitle == right.ChallengeTitle && left.Direction == right.Direction && left.ReasonCode == right.ReasonCode;
    public static async Task<StaffWebhookStream> StreamAsync(NoCtfDbContext db, Guid id, CancellationToken ct)
    {
        var stream = await FindStreamAsync(db, id, ct);
        if (stream is not null) return stream;
        stream = new() { CompetitionId = id }; db.StaffWebhookStreams.Add(stream); return stream;
    }
    private static Task<StaffWebhookStream?> FindStreamAsync(NoCtfDbContext db, Guid id, CancellationToken ct) =>
        db.StaffWebhookStreams.Local.FirstOrDefault(value => value.CompetitionId == id) is { } stream
            ? Task.FromResult<StaffWebhookStream?>(stream)
            : db.StaffWebhookStreams.SingleOrDefaultAsync(value => value.CompetitionId == id, ct);
    public static StaffWebhookEvent AddEvent(NoCtfDbContext db, StaffWebhookStream stream, StaffWebhookEventKind kind,
        DateTimeOffset now, StaffWorkItemChangeKind? change = null, IReadOnlyList<StaffWorkItemSummary>? items = null,
        Guid? targetId = null, long? sequence = null)
    {
        var row = new StaffWebhookEvent { Id = Guid.CreateVersion7(), CompetitionId = stream.CompetitionId,
            Sequence = sequence ?? checked(++stream.Sequence), LatestBusinessSequence = stream.LatestBusinessSequence,
            Kind = kind, ChangeKind = change, OccurredAt = now, TargetId = targetId };
        row.Items = (items ?? []).Select((value, index) => new StaffWebhookEventItem { EventId = row.Id, Position = index, Summary = value.Copy() }).ToList();
        db.StaffWebhookEvents.Add(row); return row;
    }
}

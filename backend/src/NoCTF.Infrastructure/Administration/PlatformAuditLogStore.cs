using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAuditLogStore(NoCtfDbContext db) : IPlatformAuditLogStore
{
    public async Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken ct)
    {
        var items = new List<PlatformAuditView>(query.Limit * 2);
        if (query.Kind is not PlatformAuditKind.UserAccountLifecycle)
        {
            var competitionEvents = db.CompetitionEvents.AsNoTracking();
            if (query.From is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt >= query.From.Value);
            if (query.To is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt <= query.To.Value);
            if (query.CompetitionId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.CompetitionId == query.CompetitionId.Value);
            if (query.ActorId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.ActorUserId == query.ActorId.Value);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt < beforeOccurredAt
                    || item.OccurredAt == beforeOccurredAt
                    && item.Id.CompareTo(beforeId) < 0);
            }
            competitionEvents = query.Kind switch
            {
                PlatformAuditKind.CompetitionLifecycle => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.CompetitionLifecycleChanged),
                PlatformAuditKind.CompetitionLeaderboardVisibility => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.LeaderboardVisibilityChanged),
                PlatformAuditKind.CompetitionEvent => competitionEvents.Where(item =>
                    item.Kind != CompetitionEventKind.CompetitionLifecycleChanged
                    && item.Kind != CompetitionEventKind.LeaderboardVisibilityChanged),
                _ => competitionEvents
            };

            var eventItems = await competitionEvents
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct);
            var competitionIds = eventItems
                .Select(item => item.CompetitionId)
                .Distinct()
                .ToArray();
            var competitionTitles = await db.Competitions.AsNoTracking()
                .Where(item => competitionIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
            items.AddRange(eventItems.Select(item => new PlatformAuditView(
                item.Id,
                MapKind(item.Kind),
                item.CompetitionId,
                item.CompetitionId,
                item.ActorUserId,
                null,
                item.CompetitionStatus,
                null,
                item.LeaderboardVisibility,
                null,
                item.Kind,
                item.Level,
                item.Visibility,
                item.RelatedUserId,
                item.TeamId,
                item.CompetitionChallengeId,
                item.RuntimeInstanceId,
                item.SubmissionId,
                item.ScoringEventId,
                item.QuestionId,
                item.SubmissionKind,
                item.SubmissionState,
                item.ScoringEventKind,
                item.ScoringResult,
                competitionTitles.GetValueOrDefault(item.CompetitionId),
                item.Reason,
                false,
                item.OccurredAt)));
        }

        if (query.CompetitionId is null
            && query.Kind is (null or PlatformAuditKind.UserAccountLifecycle))
        {
            var accountAudits = db.UserAccountLifecycleAudits.AsNoTracking();
            if (query.From is not null)
                accountAudits = accountAudits.Where(audit =>
                    audit.OccurredAt >= query.From.Value);
            if (query.To is not null)
                accountAudits = accountAudits.Where(audit =>
                    audit.OccurredAt <= query.To.Value);
            if (query.ActorId is not null)
                accountAudits = accountAudits.Where(audit =>
                    audit.ActorUserId == query.ActorId.Value);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                accountAudits = accountAudits.Where(audit =>
                    audit.OccurredAt < beforeOccurredAt
                    || audit.OccurredAt == beforeOccurredAt
                    && audit.Id.CompareTo(beforeId) < 0);
            }

            items.AddRange((await accountAudits
                .OrderByDescending(audit => audit.OccurredAt)
                .ThenByDescending(audit => audit.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct))
                .Select(audit => new PlatformAuditView(
                    audit.Id,
                    PlatformAuditKind.UserAccountLifecycle,
                    audit.TargetUserId,
                    null,
                    audit.ActorUserId,
                    null,
                    null,
                    null,
                    null,
                    audit.Action,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    audit.TargetUserName,
                    audit.Reason,
                    false,
                    audit.OccurredAt)));
        }

        return items
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit)
            .ToArray();
    }

    private static PlatformAuditKind MapKind(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionLifecycleChanged =>
            PlatformAuditKind.CompetitionLifecycle,
        CompetitionEventKind.LeaderboardVisibilityChanged =>
            PlatformAuditKind.CompetitionLeaderboardVisibility,
        _ => PlatformAuditKind.CompetitionEvent
    };

}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAuditLogStore(NoCtfDbContext db) : IPlatformAuditLogStore
{
    public async Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken ct)
    {
        var items = new List<PlatformAuditView>(query.Limit * 2);
        if (query.Kind is null or PlatformAuditKind.CompetitionLifecycle)
        {
            var competitionAudits = db.Set<CompetitionLifecycleAudit>().AsNoTracking();
            if (query.From is not null)
                competitionAudits = competitionAudits.Where(audit =>
                    audit.OccurredAt >= query.From.Value);
            if (query.To is not null)
                competitionAudits = competitionAudits.Where(audit =>
                    audit.OccurredAt <= query.To.Value);
            if (query.CompetitionId is not null)
                competitionAudits = competitionAudits.Where(audit =>
                    audit.CompetitionId == query.CompetitionId.Value);
            if (query.ActorId is not null)
                competitionAudits = competitionAudits.Where(audit =>
                    audit.ActorId == query.ActorId.Value);

            items.AddRange((await competitionAudits
                .OrderByDescending(audit => audit.OccurredAt)
                .ThenByDescending(audit => audit.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct))
                .Select(audit => new PlatformAuditView(
                    audit.Id,
                    PlatformAuditKind.CompetitionLifecycle,
                    audit.CompetitionId,
                    audit.CompetitionId,
                    audit.ActorId,
                    audit.From,
                    audit.To,
                    null,
                    null,
                    audit.Reason,
                    audit.Automatic,
                    audit.OccurredAt)));
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
                    audit.Action,
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
}

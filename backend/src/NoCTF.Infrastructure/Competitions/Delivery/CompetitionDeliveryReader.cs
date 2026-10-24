using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Delivery;
using NoCTF.Application.Runtime.Flags;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Delivery;

public sealed class CompetitionDeliveryReader(NoCtfDbContext db, IChallengeRuntimeTemplateCatalog runtimeTemplates) : ICompetitionDeliveryReader
{
    public async Task<CompetitionDeliveryPage?> ReadAsync(CompetitionDeliveryQuery query, CancellationToken ct)
    {
        var mode = await db.Competitions.Where(item => item.Id == query.CompetitionId).Select(item => (GameMode?)item.Mode).SingleOrDefaultAsync(ct);
        if (mode is null or GameMode.LiveSolo) return null;
        var challenges = db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == query.CompetitionId
                && (query.CompetitionChallengeId == null || item.Id == query.CompetitionChallengeId))
            .Join(db.Challenges.AsNoTracking(), item => item.ChallengeId, template => template.Id,
                (item, template) => new { item.Id, item.ChallengeId, Title = item.CustomTitle ?? template.Title, item.Order });
        var teams = db.Teams.AsNoTracking().Where(item => item.CompetitionId == query.CompetitionId
            && (query.TeamId == null || item.Id == query.TeamId));
        var pairs = teams.SelectMany(_ => challenges, (team, challenge) => new
        {
            TeamId = team.Id, TeamName = team.Name, CompetitionChallengeId = challenge.Id,
            challenge.ChallengeId, challenge.Title, challenge.Order
        });
        var total = await pairs.CountAsync(ct);
        var ordered = query.Desc
            ? pairs.OrderByDescending(item => item.TeamName).ThenByDescending(item => item.TeamId).ThenBy(item => item.Order).ThenBy(item => item.CompetitionChallengeId)
            : pairs.OrderBy(item => item.TeamName).ThenBy(item => item.TeamId).ThenBy(item => item.Order).ThenBy(item => item.CompetitionChallengeId);
        var page = await ordered.Skip(query.Offset).Take(query.Limit).ToArrayAsync(ct);
        if (page.Length == 0) return new([], total);
        var entryIds = page.Select(item => item.CompetitionChallengeId).Distinct().ToArray();
        var templateIds = page.Select(item => item.ChallengeId).Distinct().ToArray();
        var teamIds = page.Select(item => item.TeamId).Distinct().ToArray();
        var templates = await db.Challenges.AsNoTracking().Where(item => templateIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Definition }).AsSplitQuery().ToDictionaryAsync(item => item.Id, ct);
        var randomTemplates = (await db.ChallengeFlags.AsNoTracking().Where(flag => flag.ChallengeId != null
            && templateIds.Contains(flag.ChallengeId.Value) && flag.CompetitionChallengeId == null
            && flag.TeamId == null && flag.SpecificationKind == SpecificationKind.Attachment)
            .Select(flag => flag.ChallengeId!.Value).Distinct().ToArrayAsync(ct)).ToHashSet();
        var flags = await db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking().Where(flag =>
            flag.ChallengeId != null && templateIds.Contains(flag.ChallengeId.Value)
                && flag.CompetitionChallengeId == null && flag.TeamId == null && flag.SpecificationKind == null
            || flag.ChallengeId == null && flag.CompetitionChallengeId != null && entryIds.Contains(flag.CompetitionChallengeId.Value)
                && (flag.TeamId == null || teamIds.Contains(flag.TeamId.Value)))
            .Where(flag => query.IncludeHistory || flag.DeletedAt == null
                && (flag.ValidStart == null || flag.ValidStart <= query.Now) && (flag.ValidUntil == null || flag.ValidUntil > query.Now))
            .OrderByDescending(flag => flag.CreatedAt).ThenByDescending(flag => flag.Id).ToArrayAsync(ct);
        var runtimeIds = flags.Where(flag => flag.SpecificationKind == SpecificationKind.RuntimeInstance && flag.SpecificationId != null)
            .Select(flag => flag.SpecificationId!.Value).Distinct().ToArray();
        var runtimes = await db.RuntimeInstances.AsNoTracking().Where(runtime => runtimeIds.Contains(runtime.Id)
            && runtime.CompetitionId == query.CompetitionId).ToDictionaryAsync(runtime => runtime.Id, ct);
        var attachments = await db.Set<ChallengeAttachment>().IgnoreQueryFilters().AsNoTracking()
            .Where(item => templateIds.Contains(item.ChallengeId))
            .Select(item => new { item.Id, item.ChallengeId, item.File.FileName, item.DeletedAt, item.CreatedAt })
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToArrayAsync(ct);
        var results = page.Select(item =>
        {
            var template = templates[item.ChallengeId];
            var random = randomTemplates.Contains(item.ChallengeId);
            var perTeam = runtimeTemplates.Get(template.Definition)?.FlagSource == NoCTF.Application.Runtime.Provisioning.RuntimeFlagSource.PerTeam;
            var staticFlags = StaticFlagConfiguration.SupportsManualFlags(template.Definition, runtimeTemplates);
            var selected = flags.Where(flag => (flag.CompetitionChallengeId == item.CompetitionChallengeId
                    || flag.ChallengeId == item.ChallengeId) && (flag.TeamId == null || flag.TeamId == item.TeamId))
                .Where(flag => flag.SpecificationKind switch
                {
                    SpecificationKind.Attachment => flag.TeamId == item.TeamId && random,
                    SpecificationKind.RuntimeDefinition => flag.TeamId == item.TeamId && flag.SpecificationId == item.CompetitionChallengeId
                        && (mode == GameMode.Koh || mode == GameMode.Ctf && perTeam),
                    SpecificationKind.AwdRound => flag.TeamId == item.TeamId && mode == GameMode.Awd,
                    SpecificationKind.RuntimeInstance => flag.TeamId == item.TeamId && flag.SpecificationId is Guid runtimeId
                        && runtimes.TryGetValue(runtimeId, out var runtime) && runtime.TeamId == item.TeamId
                        && runtime.CompetitionChallengeId == item.CompetitionChallengeId
                        && runtime.Purpose is RuntimePurpose.Player or RuntimePurpose.AwdpAttack or RuntimePurpose.Practice,
                    null => mode == GameMode.Ctf && staticFlags && !random,
                    _ => false
                })
                .Select(flag => new CompetitionDeliveryFlag(flag.Id, flag.Flag, flag.MatchKind, State(flag, query.Now, runtimes),
                    flag.SpecificationKind == SpecificationKind.Attachment ? flag.SpecificationId : null,
                    flag.SpecificationKind == SpecificationKind.RuntimeInstance ? flag.SpecificationId : null,
                    flag.CreatedAt, flag.ValidStart, flag.ValidUntil))
                .Where(flag => query.IncludeHistory || flag.State == RuntimeFlagState.Active).ToArray();
            var assigned = selected.Where(flag => flag.AttachmentId != null).Select(flag => flag.AttachmentId!.Value).ToHashSet();
            var files = attachments.Where(attachment => attachment.ChallengeId == item.ChallengeId
                    && (random ? assigned.Contains(attachment.Id) : attachment.DeletedAt == null))
                .Select(attachment => new CompetitionDeliveryAttachment(attachment.Id, attachment.FileName, attachment.DeletedAt != null)).ToArray();
            return new CompetitionDeliveryItem(item.TeamId, item.TeamName, item.CompetitionChallengeId, item.Title, random, files, selected);
        }).ToArray();
        return new(results, total);
    }

    private static RuntimeFlagState State(ChallengeFlag flag, DateTimeOffset now, IReadOnlyDictionary<Guid, RuntimeInstance> runtimes)
    {
        if (flag.DeletedAt != null) return RuntimeFlagState.Deleted;
        if (flag.ValidUntil <= now) return RuntimeFlagState.Expired;
        if (flag.ValidStart > now) return RuntimeFlagState.Scheduled;
        if (flag.SpecificationKind == SpecificationKind.RuntimeInstance && flag.SpecificationId is Guid id && runtimes.TryGetValue(id, out var runtime))
            return runtime.State == RuntimeState.Running ? RuntimeFlagState.Active
                : runtime.State is RuntimeState.Queued or RuntimeState.Provisioning ? RuntimeFlagState.Scheduled : RuntimeFlagState.Expired;
        return RuntimeFlagState.Active;
    }
}

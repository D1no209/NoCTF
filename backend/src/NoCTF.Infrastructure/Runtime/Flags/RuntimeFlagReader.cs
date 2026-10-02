using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Flags;

public sealed class RuntimeFlagReader(NoCtfDbContext db) : IRuntimeFlagReader
{
    public async Task<RuntimeFlagScope?> FindScopeAsync(Guid runtimeInstanceId, Guid actorId, bool isAdministrator, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking().Where(item => item.Id == runtimeInstanceId)
            .Select(item => new { item.CompetitionId, item.ChallengeId }).SingleOrDefaultAsync(ct);
        if (runtime is null) return null;
        var canManage = runtime.ChallengeId is Guid challengeId && await db.Challenges.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(challenge => challenge.Id == challengeId && (isAdministrator || challenge.OwnerId == actorId
                || challenge.Managers.Any(manager => manager.UserId == actorId)), ct);
        return new(runtime.CompetitionId, runtime.ChallengeId, canManage);
    }

    public async Task<RuntimeFlagPage> ReadAsync(RuntimeFlagQuery query, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking().SingleAsync(item => item.Id == query.RuntimeInstanceId, ct);
        var flags = db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking();
        if (runtime.Purpose == RuntimePurpose.TemplateTest)
        {
            flags = flags.Where(flag => flag.ChallengeId == runtime.ChallengeId
                && flag.CompetitionChallengeId == null && flag.TeamId == null
                && flag.SpecificationKind == SpecificationKind.RuntimeInstance && flag.SpecificationId == runtime.Id);
        }
        else if (runtime.Purpose is RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget
            || runtime.CompetitionChallengeId is null)
            return new([], 0);
        else
        {
            var scope = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.Id == runtime.CompetitionChallengeId && item.CompetitionId == runtime.CompetitionId)
                .Join(db.Challenges.IgnoreQueryFilters(), item => item.ChallengeId, template => template.Id,
                    (item, template) => new { item.ChallengeId, template.Mode }).SingleOrDefaultAsync(ct);
            if (scope is null || scope.Mode == GameMode.Koh) return new([], 0);
            var start = runtime.RunningAt ?? runtime.CreatedAt;
            var end = runtime.StoppedAt ?? query.Now;
            var stopped = runtime.StoppedAt is not null;
            flags = flags.Where(flag =>
                // Runtime-specific flags must never spill into another generation.
                (flag.CompetitionChallengeId == runtime.CompetitionChallengeId && flag.ChallengeId == null
                    && flag.TeamId == runtime.TeamId && flag.SpecificationKind == SpecificationKind.RuntimeInstance
                    && flag.SpecificationId == runtime.Id)
                || ((stopped ? (flag.ValidStart ?? flag.CreatedAt) < end : (flag.ValidStart ?? flag.CreatedAt) <= end)
                    && (flag.ValidUntil == null || flag.ValidUntil > start)
                    && ((scope.Mode == GameMode.Ctf && runtime.TeamId != null
                            && flag.CompetitionChallengeId == runtime.CompetitionChallengeId && flag.ChallengeId == null
                            && flag.TeamId == runtime.TeamId && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                            && flag.SpecificationId == runtime.CompetitionChallengeId)
                        || (scope.Mode == GameMode.Awd && runtime.TeamId != null
                            && flag.CompetitionChallengeId == runtime.CompetitionChallengeId && flag.ChallengeId == null
                            && flag.TeamId == runtime.TeamId && flag.SpecificationKind == SpecificationKind.AwdRound)
                        || (scope.Mode == GameMode.Ctf && flag.TeamId == null && flag.SpecificationKind == null
                            && ((flag.ChallengeId == scope.ChallengeId && flag.CompetitionChallengeId == null)
                                || (flag.CompetitionChallengeId == runtime.CompetitionChallengeId && flag.ChallengeId == null))))));
        }
        if (!query.IncludeHistory)
            flags = flags.Where(flag => flag.DeletedAt == null && (flag.ValidStart == null || flag.ValidStart <= query.Now)
                && (flag.ValidUntil == null || flag.ValidUntil > query.Now));
        var total = await flags.CountAsync(ct);
        var ordered = query.Desc
            ? flags.OrderByDescending(flag => flag.CreatedAt).ThenByDescending(flag => flag.Id)
            : flags.OrderBy(flag => flag.CreatedAt).ThenBy(flag => flag.Id);
        var page = await ordered.Skip(query.Offset).Take(query.Limit).ToListAsync(ct);
        return new(page.Select(flag => new RuntimeFlagView(
            new(flag.Id, flag.ChallengeId, flag.CompetitionChallengeId, flag.TeamId, flag.Flag, flag.MatchKind,
                flag.SpecificationKind, flag.SpecificationId, flag.ValidStart, flag.ValidUntil, flag.DeletedAt, flag.CreatedAt),
            flag.SpecificationKind switch
            {
                SpecificationKind.RuntimeInstance => RuntimeFlagSource.Instance,
                SpecificationKind.RuntimeDefinition => RuntimeFlagSource.Team,
                SpecificationKind.AwdRound => RuntimeFlagSource.AwdRound,
                _ => RuntimeFlagSource.Static
            },
            flag.DeletedAt is not null ? RuntimeFlagState.Deleted
                : flag.ValidUntil <= query.Now ? RuntimeFlagState.Expired
                : flag.ValidStart > query.Now ? RuntimeFlagState.Scheduled : RuntimeFlagState.Active)).ToArray(), total);
    }
}

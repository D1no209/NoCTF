using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdCheckerTaskReader(NoCtfDbContext db, PlatformSecretProtector secrets) : IAwdCheckerTaskReader
{
    public async Task<AwdCheckerTask?> ReadAsync(Guid runtimeId, Guid factId, Guid attemptId, DateTimeOffset at, CancellationToken ct)
    {
        var fact = await db.GameplayFacts.AsNoTracking().Include(item => item.Awd).ThenInclude(details => details!.Flags)
            .SingleOrDefaultAsync(item => item.Id == factId && item.Kind == GameplayFactKind.AwdServiceTransition, ct);
        if (fact?.Awd is not { Execution: AwdCheckExecution.Running, AttemptDeadline: { } deadline } details
            || details.RuntimeId != runtimeId || details.AttemptId != attemptId || at >= deadline)
            return null;
        var ids = details.Flags.Select(flag => flag.FlagId).ToArray();
        var flags = await db.ChallengeFlags.AsNoTracking().Include(flag => flag.AwdIssue)
            .Where(flag => ids.Contains(flag.Id) && flag.DeletedAt == null).ToDictionaryAsync(flag => flag.Id, ct);
        var rulesIds = flags.Values.Where(flag => flag.AwdIssue != null).Select(flag => flag.AwdIssue!.RulesId).Distinct().ToArray();
        var rules = await db.AwdRoundRules.AsNoTracking().Where(rule => rulesIds.Contains(rule.Id))
            .ToDictionaryAsync(rule => rule.Id, ct);
        var result = new List<AwdCheckerTaskFlag>(ids.Length);
        foreach (var expected in details.Flags)
        {
            if (!flags.TryGetValue(expected.FlagId, out var flag) || flag.AwdIssue is not { } issue
                || issue.CompetitionId != fact.CompetitionId || issue.CompetitionChallengeId != fact.CompetitionChallengeId
                || issue.TeamId != fact.TeamId || issue.State != AwdFlagIssueState.Issued)
                return null;
            var store = rules[issue.RulesId].Stores.Single(item => item.StoreId == issue.StoreId);
            result.Add(new(flag.Id, issue.StoreId, store.Name, store.TargetServiceName, flag.Flag, expected.Historical,
                expected.RequiresWrite, issue.ProtectedReadContext is { } context
                    ? secrets.Unprotect(Convert.FromBase64String(context), PlatformSecretPurpose.AwdFlagReadContext, issue.CompetitionId, issue.FlagId)
                    : null, issue.PublicLocator));
        }
        return new(factId, attemptId, deadline, result);
    }
}

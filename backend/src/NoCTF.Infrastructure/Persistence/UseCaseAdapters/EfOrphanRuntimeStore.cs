using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfOrphanRuntimeStore(
    NoCtfDbContext db,
    RuntimeOperationPolicyOptions policyOptions) : IOrphanRuntimeStore
{
    public async Task<IReadOnlyList<RuntimeCleanupTarget>> ListOrphansAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var instances = await db.ChallengeInstances.AsNoTracking()
            .Where(instance => instance.Status != RuntimeStatus.Stopped)
            .OrderBy(instance => instance.CreatedAt)
            .ThenBy(instance => instance.Id)
            .ToListAsync(cancellationToken);
        var validCompetitions = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Status == CompetitionStatus.Running
                                  || competition.Status == CompetitionStatus.Paused)
            .Select(competition => competition.Id)
            .ToHashSetAsync(cancellationToken);
        var validChallenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.IsPublished && !challenge.Deletion.IsDeleted)
            .Select(challenge => new { challenge.Id, challenge.CompetitionId })
            .ToDictionaryAsync(challenge => challenge.Id, challenge => challenge.CompetitionId, cancellationToken);
        var validTeams = await db.Teams.AsNoTracking()
            .Where(team => team.RegistrationStatus == TeamRegistrationStatus.Approved && !team.Ban.IsBanned)
            .Select(team => new { team.Id, team.CompetitionId })
            .ToDictionaryAsync(team => team.Id, team => team.CompetitionId, cancellationToken);

        var latest = instances
            .Where(instance => IsValid(instance, validCompetitions, validChallenges, validTeams, now))
            .GroupBy(instance => new { instance.CompetitionId, instance.CompetitionChallengeId, instance.TeamId })
            .ToDictionary(
                group => (group.Key.CompetitionId, group.Key.CompetitionChallengeId, group.Key.TeamId),
                group => group.OrderByDescending(instance => instance.CreatedAt)
                    .ThenByDescending(instance => instance.Id)
                    .First().Id);
        return instances
            .Where(instance => !IsValid(instance, validCompetitions, validChallenges, validTeams, now)
                               || latest.GetValueOrDefault((instance.CompetitionId, instance.CompetitionChallengeId, instance.TeamId)) != instance.Id)
            .Select(instance => new RuntimeCleanupTarget(
                instance.Id,
                string.IsNullOrWhiteSpace(instance.Receipt)
                    ? null
                    : JsonSerializer.Deserialize<ContainerReceipt>(instance.Receipt)
                      ?? throw new InvalidOperationException($"Runtime receipt for instance {instance.Id} is invalid.")))
            .ToList();
    }

    public async Task MarkStoppedAsync(
        IReadOnlyCollection<Guid> instanceIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.ChallengeInstances.Where(instance => instanceIds.Contains(instance.Id))
            .ExecuteUpdateAsync(update => update
                .SetProperty(instance => instance.Status, RuntimeStatus.Stopped)
                .SetProperty(instance => instance.UpdatedAt, now), cancellationToken);
        await db.ChallengeFlags
            .Where(flag => flag.ChallengeInstanceId != null
                           && instanceIds.Contains(flag.ChallengeInstanceId.Value)
                           && (flag.ValidEnd == null || flag.ValidEnd > now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(flag => flag.ValidEnd, now)
                .SetProperty(flag => flag.UpdatedAt, now)
                .SetProperty(flag => flag.RowVersion, flag => flag.RowVersion + 1), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private bool IsValid(
        ChallengeInstance instance,
        IReadOnlySet<Guid> validCompetitions,
        IReadOnlyDictionary<Guid, Guid> validChallenges,
        IReadOnlyDictionary<Guid, Guid> validTeams,
        DateTimeOffset now) =>
        validCompetitions.Contains(instance.CompetitionId)
        && IsPreparedPlaceholderWithinLease(instance, now, policyOptions.ClaimLeaseGrace)
        && instance.Status != RuntimeStatus.Failed
        && validChallenges.GetValueOrDefault(instance.CompetitionChallengeId) == instance.CompetitionId
        && (instance.TeamId is null
            || validTeams.GetValueOrDefault(instance.TeamId.Value) == instance.CompetitionId)
        && (instance.ExpiresAt is null || instance.ExpiresAt > now);

    public static bool IsPreparedPlaceholderWithinLease(
        ChallengeInstance instance,
        DateTimeOffset now,
        TimeSpan claimLeaseGrace) =>
        !string.IsNullOrWhiteSpace(instance.Receipt)
        || instance.UpdatedAt > now.Subtract(
            RuntimeOperationPolicyOptions.MaximumOperationTimeout + claimLeaseGrace);
}

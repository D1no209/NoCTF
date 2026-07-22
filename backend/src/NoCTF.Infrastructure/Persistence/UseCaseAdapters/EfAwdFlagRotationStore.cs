using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfAwdFlagRotationStore(NoCtfDbContext db) : IAwdFlagRotationStore
{
    public async Task<IReadOnlyList<AwdFlagRotationTarget>> ListRunningAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ChallengeInstances.AsNoTracking()
            .Join(db.Competitions.AsNoTracking(), instance => instance.CompetitionId, competition => competition.Id,
                (instance, competition) => new { Instance = instance, Competition = competition })
            .Join(db.CompetitionChallenges.AsNoTracking(), item => item.Instance.CompetitionChallengeId, challenge => challenge.Id,
                (item, challenge) => new { item.Instance, item.Competition, Challenge = challenge })
            .Join(db.Teams.AsNoTracking(), item => item.Instance.TeamId, team => (Guid?)team.Id,
                (item, team) => new { item.Instance, item.Competition, item.Challenge, Team = team })
            .Where(item => item.Competition.Mode == GameMode.Awd
                           && item.Competition.Status == CompetitionStatus.Running
                           && item.Instance.Status == RuntimeStatus.Running && item.Instance.TeamId != null
                           && item.Instance.Receipt != string.Empty
                           && item.Challenge.IsPublished && !item.Challenge.Deletion.IsDeleted
                           && item.Team.RegistrationStatus == TeamRegistrationStatus.Approved
                           && !item.Team.Deletion.IsDeleted && !item.Team.Ban.IsBanned)
            .OrderBy(item => item.Competition.Id).ThenBy(item => item.Challenge.Id).ThenBy(item => item.Team.Id)
            .Select(item => new
            {
                CompetitionId = item.Competition.Id,
                CompetitionChallengeId = item.Challenge.Id,
                TeamId = item.Team.Id,
                ChallengeInstanceId = item.Instance.Id,
                ChallengeRevision = item.Challenge.Revision,
                item.Competition.StartTime,
                CompetitionConfigurationJson = item.Competition.ConfigurationJson,
                ChallengeConfigurationJson = item.Challenge.ConfigurationJson,
                item.Instance.Receipt
            })
            .ToListAsync(cancellationToken);
        return rows.Select(item => new AwdFlagRotationTarget(
            item.CompetitionId,
            item.CompetitionChallengeId,
            item.TeamId,
            item.ChallengeInstanceId,
            item.ChallengeRevision,
            item.StartTime,
            item.CompetitionConfigurationJson,
            item.ChallengeConfigurationJson,
            JsonSerializer.Deserialize<ContainerReceipt>(item.Receipt)
                ?? throw new InvalidOperationException("Persisted AWD runtime receipt is invalid.")))
            .ToList();
    }

    public async Task<AwdFlagInjectionClaim?> TryClaimAsync(
        AwdFlagRotationTarget target,
        DateTimeOffset validStart,
        DateTimeOffset validEnd,
        string flag,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var status = await CompetitionWriteLock.AcquireAsync(db, target.CompetitionId, cancellationToken);
        if (status != CompetitionStatus.Running) return null;
        var existing = await db.ChallengeFlags.SingleOrDefaultAsync(item =>
            item.CompetitionId == target.CompetitionId
            && item.CompetitionChallengeId == target.CompetitionChallengeId
            && item.TeamId == target.TeamId
            && item.StageId == null
            && item.ValidStart == validStart, cancellationToken);
        if (existing is not null)
        {
            if ((existing.Status == ChallengeFlagStatus.Active
                 && (existing.ValidEnd == null || existing.ValidEnd > now))
                || (existing.Status == ChallengeFlagStatus.PendingInjection
                    && existing.InjectionClaimedAt > staleBefore))
                return null;
            existing.ChallengeInstanceId = target.ChallengeInstanceId;
            existing.ValidStart = validStart;
            existing.ValidEnd = validEnd;
            existing.Status = ChallengeFlagStatus.PendingInjection;
            existing.InjectionClaimToken = Guid.CreateVersion7(now);
            existing.InjectionClaimedAt = now;
            existing.UpdatedAt = now;
            existing.RowVersion++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, existing.InjectionClaimToken.Value, existing.Flag);
        }

        var claimToken = Guid.CreateVersion7(now);
        var entity = new ChallengeFlag
        {
            Id = Guid.CreateVersion7(now.AddTicks(1)),
            CompetitionId = target.CompetitionId,
            CompetitionChallengeId = target.CompetitionChallengeId,
            TeamId = target.TeamId,
            ChallengeInstanceId = target.ChallengeInstanceId,
            Flag = flag,
            ValidStart = validStart,
            ValidEnd = validEnd,
            Status = ChallengeFlagStatus.PendingInjection,
            InjectionClaimToken = claimToken,
            InjectionClaimedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ChallengeFlags.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(entity.Id, claimToken, entity.Flag);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_challenge_flags_CompetitionId_CompetitionChallengeId_TeamId_ValidStart"
        })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<bool> ActivateAsync(
        Guid competitionId,
        AwdFlagInjectionClaim claim,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, cancellationToken) != CompetitionStatus.Running)
            return false;
        var updated = await db.ChallengeFlags
            .Where(item => item.Id == claim.FlagId
                           && item.Status == ChallengeFlagStatus.PendingInjection
                           && item.InjectionClaimToken == claim.ClaimToken)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, ChallengeFlagStatus.Active)
                .SetProperty(item => item.InjectionClaimToken, (Guid?)null)
                .SetProperty(item => item.UpdatedAt, now)
                .SetProperty(item => item.RowVersion, item => item.RowVersion + 1), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated == 1;
    }

    public async Task<bool> MarkRuntimeFailedAsync(
        Guid competitionId,
        Guid challengeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = await CompetitionWriteLock.AcquireAsync(db, competitionId, cancellationToken);
        var instanceUpdated = await db.ChallengeInstances
            .Where(instance => instance.Id == challengeInstanceId
                               && instance.CompetitionId == competitionId
                               && instance.Status == RuntimeStatus.Running)
            .ExecuteUpdateAsync(update => update
                .SetProperty(instance => instance.Status, RuntimeStatus.Failed)
                .SetProperty(instance => instance.UpdatedAt, now), cancellationToken);
        if (instanceUpdated != 1) return false;

        var operationUpdated = await db.RuntimeOperations
            .Where(operation => operation.CompetitionId == competitionId
                                && operation.ChallengeInstanceId == challengeInstanceId
                                && operation.Status == RuntimeStatus.Running)
            .ExecuteUpdateAsync(update => update
                .SetProperty(operation => operation.Status, RuntimeStatus.Failed)
                .SetProperty(operation => operation.UpdatedAt, now), cancellationToken);
        if (operationUpdated != 1) return false;
        await RuntimeFailurePersistence.InvalidateFlagsAsync(
            db, challengeInstanceId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

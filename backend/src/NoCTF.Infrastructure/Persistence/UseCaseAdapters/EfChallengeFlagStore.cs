using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeFlagStore(NoCtfDbContext db) : IChallengeFlagStore
{
    public async Task<ChallengeFlagScope?> LoadScopeAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        CancellationToken ct)
    {
        var status = await (
            from challenge in db.Challenges.AsNoTracking()
            join competition in db.Competitions.AsNoTracking() on challenge.CompetitionId equals competition.Id
            where challenge.Id == challengeId
                  && challenge.CompetitionId == competitionId
                  && !challenge.Deletion.IsDeleted
                  && !competition.Deletion.IsDeleted
            select (CompetitionStatus?)competition.Status)
            .SingleOrDefaultAsync(ct);
        if (status is null)
            return null;

        var teamExists = teamId is null || await db.Teams.AsNoTracking().AnyAsync(
            team => team.Id == teamId
                    && team.CompetitionId == competitionId
                    && !team.Deletion.IsDeleted,
            ct);
        return new ChallengeFlagScope(status.Value, teamExists);
    }

    public async Task<IReadOnlyList<ChallengeFlagView>> ListAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var flags = await Query()
            .Where(flag => flag.CompetitionId == competitionId && flag.ChallengeId == challengeId)
            .OrderBy(flag => flag.TeamId)
            .ThenBy(flag => flag.ValidStart)
            .ThenBy(flag => flag.Id)
            .ToListAsync(ct);
        return flags.Select(Map).ToList();
    }

    public async Task<ChallengeFlagView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        CancellationToken ct)
    {
        var flag = await Query()
            .Where(flag => flag.Id == flagId
                           && flag.CompetitionId == competitionId
                           && flag.ChallengeId == challengeId)
            .SingleOrDefaultAsync(ct);
        return flag is null ? null : Map(flag);
    }

    public async Task<ChallengeFlagMutationResult> CreateAsync(
        CreateChallengeFlagCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, ChallengeFlagMutationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(null, ChallengeFlagMutationFailure.FlagLocked);
        var scopeError = await ValidateMutableScopeAsync(
            command.CompetitionId,
            command.ChallengeId,
            command.TeamId,
            ct);
        if (scopeError is not null)
            return new(null, scopeError);
        if (await OverlapsAsync(
                command.CompetitionId,
                command.ChallengeId,
                command.TeamId,
                command.ValidStart,
                command.ValidEnd,
                excludedFlagId: null,
                ct))
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);

        var entity = new ChallengeFlag
        {
            Id = Guid.CreateVersion7(command.CreatedAt),
            CompetitionId = command.CompetitionId,
            ChallengeId = command.ChallengeId,
            TeamId = command.TeamId,
            Flag = command.Flag,
            ValidStart = command.ValidStart,
            ValidEnd = command.ValidEnd,
            CreatedAt = command.CreatedAt,
            UpdatedAt = command.CreatedAt,
            RowVersion = 0
        };
        db.ChallengeFlags.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(Map(entity));
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);
        }
    }

    public async Task<ChallengeFlagMutationResult> UpdateAsync(
        UpdateChallengeFlagCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        if (status is null) return new(null, ChallengeFlagMutationFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished) return new(null, ChallengeFlagMutationFailure.FlagLocked);
        var entity = await db.ChallengeFlags.SingleOrDefaultAsync(
            flag => flag.Id == command.FlagId
                    && flag.CompetitionId == command.CompetitionId
                    && flag.ChallengeId == command.ChallengeId,
            ct);
        if (entity is null)
            return new(null, ChallengeFlagMutationFailure.FlagNotFound);
        if (entity.RowVersion != command.ExpectedRowVersion)
            return new(null, ChallengeFlagMutationFailure.FlagConflict);

        var scopeError = await ValidateMutableScopeAsync(
            command.CompetitionId,
            command.ChallengeId,
            command.TeamId,
            ct);
        if (scopeError is not null)
            return new(null, scopeError);
        if (await OverlapsAsync(
                command.CompetitionId,
                command.ChallengeId,
                command.TeamId,
                command.ValidStart,
                command.ValidEnd,
                command.FlagId,
                ct))
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);

        entity.TeamId = command.TeamId;
        entity.Flag = command.Flag;
        entity.ValidStart = command.ValidStart;
        entity.ValidEnd = command.ValidEnd;
        entity.UpdatedAt = command.UpdatedAt;
        entity.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(Map(entity));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(null, ChallengeFlagMutationFailure.FlagConflict);
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);
        }
    }

    public async Task<ChallengeFlagMutationFailure?> DeleteAsync(
        Guid competitionId,
        Guid challengeId,
        Guid flagId,
        long expectedRowVersion,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var status = await CompetitionWriteLock.AcquireAsync(db, competitionId, ct);
        if (status is null) return ChallengeFlagMutationFailure.CompetitionNotFound;
        if (status == CompetitionStatus.Finished) return ChallengeFlagMutationFailure.FlagLocked;
        var entity = await db.ChallengeFlags.SingleOrDefaultAsync(
            flag => flag.Id == flagId
                    && flag.CompetitionId == competitionId
                    && flag.ChallengeId == challengeId,
            ct);
        if (entity is null)
            return ChallengeFlagMutationFailure.FlagNotFound;
        if (entity.RowVersion != expectedRowVersion)
            return ChallengeFlagMutationFailure.FlagConflict;

        var scopeError = await ValidateMutableScopeAsync(competitionId, challengeId, entity.TeamId, ct);
        if (scopeError is not null)
            return scopeError;

        db.ChallengeFlags.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ChallengeFlagMutationFailure.FlagConflict;
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            return ChallengeFlagMutationFailure.FlagConflict;
        }
    }

    private async Task<ChallengeFlagMutationFailure?> ValidateMutableScopeAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        CancellationToken ct)
    {
        var scope = await LoadScopeAsync(competitionId, challengeId, teamId, ct);
        if (scope is null)
            return ChallengeFlagMutationFailure.ChallengeNotFound;
        if (scope.CompetitionStatus == CompetitionStatus.Finished)
            return ChallengeFlagMutationFailure.FlagLocked;
        if (teamId is not null && !scope.TeamExists)
            return ChallengeFlagMutationFailure.TeamNotFound;
        return null;
    }

    private Task<bool> OverlapsAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        DateTimeOffset? validStart,
        DateTimeOffset? validEnd,
        Guid? excludedFlagId,
        CancellationToken ct) =>
        db.ChallengeFlags.AsNoTracking().AnyAsync(
            flag => flag.CompetitionId == competitionId
                    && flag.ChallengeId == challengeId
                    && flag.TeamId == teamId
                    && (excludedFlagId == null || flag.Id != excludedFlagId)
                    && (flag.ValidEnd == null || validStart == null || flag.ValidEnd > validStart)
                    && (validEnd == null || flag.ValidStart == null || flag.ValidStart < validEnd),
            ct);

    private IQueryable<ChallengeFlag> Query() =>
        from flag in db.ChallengeFlags.AsNoTracking()
        join challenge in db.Challenges.AsNoTracking() on flag.ChallengeId equals challenge.Id
        join competition in db.Competitions.AsNoTracking() on flag.CompetitionId equals competition.Id
        where !challenge.Deletion.IsDeleted && !competition.Deletion.IsDeleted
        select flag;

    private static ChallengeFlagView Map(ChallengeFlag flag) => new(
        flag.Id,
        flag.CompetitionId,
        flag.ChallengeId,
        flag.TeamId,
        flag.Flag,
        flag.ValidStart,
        flag.ValidEnd,
        flag.CreatedAt,
        flag.UpdatedAt,
        flag.RowVersion);

    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };
}

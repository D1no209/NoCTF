using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Penetration.Configuration;
using Npgsql;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfChallengeFlagStore(NoCtfDbContext db) : IChallengeFlagStore
{
    public async Task<ChallengeFlagScope?> LoadScopeAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        Guid? stageId,
        Guid? challengeInstanceId,
        CancellationToken ct)
    {
        var scope = await db.CompetitionChallenges.AsNoTracking()
            .Join(db.Competitions.AsNoTracking(), challenge => challenge.CompetitionId, competition => competition.Id,
                (challenge, competition) => new { CompetitionChallenge = challenge, Competition = competition })
            .Join(db.Challenges.AsNoTracking(), item => item.CompetitionChallenge.ChallengeId, challenge => challenge.Id,
                (item, challenge) => new { item.CompetitionChallenge, item.Competition, Challenge = challenge })
            .Where(item => item.CompetitionChallenge.Id == challengeId
                           && item.CompetitionChallenge.CompetitionId == competitionId
                           && !item.CompetitionChallenge.Deletion.IsDeleted
                           && !item.Challenge.Deletion.IsDeleted
                           && !item.Competition.Deletion.IsDeleted)
            .Select(item => new { item.Competition.Status, item.Competition.Mode, Json = item.CompetitionChallenge.ConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (scope is null)
            return null;

        var teamExists = teamId is null || await db.Teams.AsNoTracking().AnyAsync(
            team => team.Id == teamId
                    && team.CompetitionId == competitionId
                    && !team.Deletion.IsDeleted,
            ct);
        var stageExists = stageId is null || scope.Mode == GameMode.Penetration
            && PenetrationConfigurationUpgrader.ParseChallenge(scope.Json).Stages.Any(stage => stage.Id == stageId);
        var instanceExists = challengeInstanceId is null || await db.ChallengeInstances.AsNoTracking().AnyAsync(
            instance => instance.Id == challengeInstanceId
                        && instance.CompetitionId == competitionId
                        && instance.CompetitionChallengeId == challengeId
                        && instance.TeamId == teamId
                        && instance.Status == RuntimeStatus.Running,
            ct);
        return new ChallengeFlagScope(scope.Status, teamExists, scope.Mode, stageExists, instanceExists);
    }

    public async Task<IReadOnlyList<ChallengeFlagView>> ListAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var flags = await Query()
            .Where(flag => flag.CompetitionId == competitionId && flag.CompetitionChallengeId == challengeId)
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
                           && flag.CompetitionChallengeId == challengeId)
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
            command.CompetitionChallengeId,
            command.TeamId,
            command.StageId,
            command.ChallengeInstanceId,
            ct);
        if (scopeError is not null)
            return new(null, scopeError);
        if (await OverlapsAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                command.TeamId,
                command.StageId,
                command.ChallengeInstanceId,
                command.ValidStart,
                command.ValidEnd,
                excludedFlagId: null,
                ct))
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);

        var entity = new ChallengeFlag
        {
            Id = Guid.CreateVersion7(command.CreatedAt),
            CompetitionId = command.CompetitionId,
            CompetitionChallengeId = command.CompetitionChallengeId,
            TeamId = command.TeamId,
            StageId = command.StageId,
            ChallengeInstanceId = command.ChallengeInstanceId,
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
                    && flag.CompetitionChallengeId == command.CompetitionChallengeId,
            ct);
        if (entity is null)
            return new(null, ChallengeFlagMutationFailure.FlagNotFound);
        if (entity.RowVersion != command.ExpectedRowVersion)
            return new(null, ChallengeFlagMutationFailure.FlagConflict);

        var scopeError = await ValidateMutableScopeAsync(
            command.CompetitionId,
            command.CompetitionChallengeId,
            command.TeamId,
            command.StageId,
            command.ChallengeInstanceId,
            ct);
        if (scopeError is not null)
            return new(null, scopeError);
        if (await OverlapsAsync(
                command.CompetitionId,
                command.CompetitionChallengeId,
                command.TeamId,
                command.StageId,
                command.ChallengeInstanceId,
                command.ValidStart,
                command.ValidEnd,
                command.FlagId,
                ct))
            return new(null, ChallengeFlagMutationFailure.FlagWindowConflict);

        entity.TeamId = command.TeamId;
        entity.StageId = command.StageId;
        entity.ChallengeInstanceId = command.ChallengeInstanceId;
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
                    && flag.CompetitionChallengeId == challengeId,
            ct);
        if (entity is null)
            return ChallengeFlagMutationFailure.FlagNotFound;
        if (entity.RowVersion != expectedRowVersion)
            return ChallengeFlagMutationFailure.FlagConflict;

        var scopeError = await ValidateMutableScopeAsync(
            competitionId, challengeId, entity.TeamId, entity.StageId, entity.ChallengeInstanceId, ct);
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
        Guid? stageId,
        Guid? challengeInstanceId,
        CancellationToken ct)
    {
        var scope = await LoadScopeAsync(
            competitionId, challengeId, teamId, stageId, challengeInstanceId, ct);
        if (scope is null)
            return ChallengeFlagMutationFailure.ChallengeNotFound;
        if (scope.CompetitionStatus == CompetitionStatus.Finished)
            return ChallengeFlagMutationFailure.FlagLocked;
        if (teamId is not null && !scope.TeamExists)
            return ChallengeFlagMutationFailure.TeamNotFound;
        if (scope.Mode == GameMode.Penetration
            && (teamId is null || stageId is null || !scope.StageExists
                || challengeInstanceId is not null && !scope.ChallengeInstanceExists)
            || scope.Mode != GameMode.Penetration
            && (stageId is not null || challengeInstanceId is not null))
            return ChallengeFlagMutationFailure.InvalidScope;
        return null;
    }

    private Task<bool> OverlapsAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? teamId,
        Guid? stageId,
        Guid? challengeInstanceId,
        DateTimeOffset? validStart,
        DateTimeOffset? validEnd,
        Guid? excludedFlagId,
        CancellationToken ct) =>
        db.ChallengeFlags.AsNoTracking().AnyAsync(
            flag => flag.CompetitionId == competitionId
                    && flag.CompetitionChallengeId == challengeId
                    && flag.TeamId == teamId
                    && flag.StageId == stageId
                    && flag.ChallengeInstanceId == challengeInstanceId
                    && (excludedFlagId == null || flag.Id != excludedFlagId)
                    && (flag.ValidEnd == null || validStart == null || flag.ValidEnd > validStart)
                    && (validEnd == null || flag.ValidStart == null || flag.ValidStart < validEnd),
            ct);

    private IQueryable<ChallengeFlag> Query() => db.ChallengeFlags.AsNoTracking()
        .Join(db.CompetitionChallenges.AsNoTracking(), flag => flag.CompetitionChallengeId, challenge => challenge.Id,
            (flag, challenge) => new { Flag = flag, CompetitionChallenge = challenge })
        .Join(db.Challenges.AsNoTracking(), item => item.CompetitionChallenge.ChallengeId, challenge => challenge.Id,
            (item, challenge) => new { item.Flag, item.CompetitionChallenge, Challenge = challenge })
        .Join(db.Competitions.AsNoTracking(), item => item.Flag.CompetitionId, competition => competition.Id,
            (item, competition) => new { item.Flag, item.CompetitionChallenge, item.Challenge, Competition = competition })
        .Where(item => !item.CompetitionChallenge.Deletion.IsDeleted
                       && !item.Challenge.Deletion.IsDeleted && !item.Competition.Deletion.IsDeleted)
        .Select(item => item.Flag);

    private static ChallengeFlagView Map(ChallengeFlag flag) => new(
        flag.Id,
        flag.CompetitionId,
        flag.CompetitionChallengeId,
        flag.TeamId,
        flag.Flag,
        flag.ValidStart,
        flag.ValidEnd,
        flag.CreatedAt,
        flag.UpdatedAt,
        flag.RowVersion,
        flag.StageId,
        flag.ChallengeInstanceId);

    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure }
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };
}

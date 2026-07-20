using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Penetration.Configuration;
using NoCTF.Infrastructure.Security;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfPenetrationRuntimeFlagPreparation(
    NoCtfDbContext db,
    PenetrationStageFlagSecretGenerator secrets) : IRuntimeFlagPreparation
{
    public async Task<ContainerRequest> PrepareAsync(
        ProvisionChallengeRuntimeCommand command,
        Guid challengeInstanceId,
        DateTimeOffset validStart,
        DateTimeOffset? validEnd,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        if (command.Mode != GameMode.Penetration)
            return request;
        if (command.TeamId is not { } teamId)
            throw new InvalidOperationException("Penetration runtimes require a Team scope.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockedStatus = await CompetitionWriteLock.AcquireAsync(
            db, command.CompetitionId, cancellationToken);
        var scope = await (
            from competition in db.Competitions
            join challenge in db.Challenges on competition.Id equals challenge.CompetitionId
            join challengeConfiguration in db.ChallengeConfigurations
                on challenge.Id equals challengeConfiguration.ChallengeId
            join team in db.Teams on competition.Id equals team.CompetitionId
            where competition.Id == command.CompetitionId
                  && challenge.Id == command.ChallengeId
                  && team.Id == teamId
            select new
            {
                competition.Status,
                competition.Mode,
                competition.EndTime,
                CompetitionDeleted = competition.Deletion.IsDeleted,
                ChallengeDeleted = challenge.Deletion.IsDeleted,
                TeamDeleted = team.Deletion.IsDeleted,
                team.RegistrationStatus,
                ConfigurationJson = challengeConfiguration.Json,
                ConfigurationRevision = challengeConfiguration.Revision
            }).SingleOrDefaultAsync(cancellationToken);
        if (lockedStatus != CompetitionStatus.Running
            || scope is null || scope.Status != CompetitionStatus.Running
            || scope.Mode != GameMode.Penetration || scope.CompetitionDeleted
            || scope.ChallengeDeleted || scope.TeamDeleted
            || scope.RegistrationStatus != TeamRegistrationStatus.Approved)
            throw new InvalidOperationException("Penetration Flag scope is not active.");
        if (command.ChallengeConfigurationRevision is { } expectedRevision
            && scope.ConfigurationRevision != expectedRevision)
            throw new InvalidOperationException("Penetration challenge configuration changed during provisioning.");

        var configuration = PenetrationConfigurationUpgrader.ParseChallenge(scope.ConfigurationJson);
        if (configuration.Stages.Count == 0)
            throw new InvalidOperationException("Penetration runtimes require at least one Stage.");

        var effectiveEnd = validEnd is { } requested && requested < scope.EndTime
            ? requested
            : scope.EndTime;
        if (effectiveEnd <= validStart)
            throw new InvalidOperationException("Penetration Flag window is empty.");

        var environment = new Dictionary<string, string>(request.Environment, StringComparer.Ordinal);
        db.ChallengeInstances.Add(new ChallengeInstance
        {
            Id = challengeInstanceId,
            CompetitionId = command.CompetitionId,
            ChallengeId = command.ChallengeId,
            TeamId = teamId,
            Provider = request.Provider,
            Receipt = string.Empty,
            Status = RuntimeStatus.Starting,
            CreatedAt = validStart,
            UpdatedAt = validStart,
            ExpiresAt = effectiveEnd
        });
        var operation = await db.RuntimeOperations.SingleAsync(
            item => item.CompetitionId == command.CompetitionId
                    && item.OperationKey == command.OperationKey
                    && item.ClaimToken == request.OperationId,
            cancellationToken);
        operation.ChallengeInstanceId = challengeInstanceId;
        operation.UpdatedAt = validStart;
        foreach (var stage in configuration.Stages.OrderBy(stage => stage.Number))
        {
            var injectionKey = stage.InjectionKey ?? $"NOCTF_STAGE_{stage.Number}_FLAG";
            if (environment.ContainsKey(injectionKey))
                throw new InvalidOperationException($"Penetration Stage injection key '{injectionKey}' is duplicated.");
            var value = secrets.Generate();
            environment[injectionKey] = value;
            db.ChallengeFlags.Add(new ChallengeFlag
            {
                Id = Guid.CreateVersion7(validStart),
                CompetitionId = command.CompetitionId,
                ChallengeId = command.ChallengeId,
                TeamId = teamId,
                StageId = stage.Id,
                ChallengeInstanceId = challengeInstanceId,
                Flag = value,
                ValidStart = validStart,
                ValidEnd = effectiveEnd,
                CreatedAt = validStart,
                UpdatedAt = validStart,
                RowVersion = 0
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return request with { Environment = environment };
    }

    public Task DeactivateAsync(
        Guid challengeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.ChallengeFlags
            .Where(flag => flag.ChallengeInstanceId == challengeInstanceId
                           && (flag.ValidEnd == null || flag.ValidEnd > now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(flag => flag.ValidEnd, now)
                .SetProperty(flag => flag.UpdatedAt, now)
                .SetProperty(flag => flag.RowVersion, flag => flag.RowVersion + 1),
                cancellationToken);
}

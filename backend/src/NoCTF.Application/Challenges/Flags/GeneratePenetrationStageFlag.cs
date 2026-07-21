using NoCTF.Application.Common;

namespace NoCTF.Application.Challenges.Flags;

public sealed record GeneratePenetrationStageFlagCommand(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid TeamId,
    Guid StageId,
    Guid ChallengeInstanceId,
    DateTimeOffset ValidStart,
    DateTimeOffset ValidEnd);

public interface IPenetrationStageFlagSecretGenerator
{
    string Generate();
}

public sealed class GeneratePenetrationStageFlag(
    IPenetrationStageFlagSecretGenerator secrets,
    CreateChallengeFlag createFlag)
{
    public Task<OperationResult<ChallengeFlagView>> ExecuteAsync(
        GeneratePenetrationStageFlagCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TeamId == Guid.Empty)
            return Task.FromResult(OperationResult<ChallengeFlagView>.Failure(
                "team_required", "A Penetration stage Flag requires a Team."));
        if (command.StageId == Guid.Empty)
            return Task.FromResult(OperationResult<ChallengeFlagView>.Failure(
                "stage_required", "A Penetration stage Flag requires a Stage."));
        if (command.ChallengeInstanceId == Guid.Empty)
            return Task.FromResult(OperationResult<ChallengeFlagView>.Failure(
                "instance_required", "A Penetration stage Flag requires a Challenge instance."));
        return createFlag.ExecuteAsync(new(
            command.CompetitionId,
            command.ChallengeId,
            command.TeamId,
            secrets.Generate(),
            command.ValidStart,
            command.ValidEnd,
            command.ValidStart,
            command.StageId,
            command.ChallengeInstanceId), cancellationToken);
    }
}

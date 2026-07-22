using NoCTF.Application.SystemProducers;
using NoCTF.GameModes.Penetration.Configuration;

namespace NoCTF.GameModes.Penetration;

public sealed class PenetrationStageConfigurationCatalog : IPenetrationStageConfigurationCatalog
{
    public PenetrationStageProgress GetProgress(
        string challengeConfigurationJson,
        IReadOnlySet<Guid> completedStageIds)
    {
        var stageIds = PenetrationConfigurationUpgrader.ParseChallenge(challengeConfigurationJson)
            .Stages
            .OrderBy(stage => stage.Number)
            .ThenBy(stage => stage.Id)
            .Select(stage => stage.Id)
            .ToArray();
        return new(stageIds.Length, stageIds.Where(stageId => !completedStageIds.Contains(stageId)).ToArray());
    }
}

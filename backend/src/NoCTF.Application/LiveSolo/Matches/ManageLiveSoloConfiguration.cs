using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Matches;

public sealed record LiveSoloConfigurationChange(LiveSoloCompetitionModeConfiguration? Configuration, LiveSoloFailure? Failure = null);
public sealed class ManageLiveSoloConfiguration(GetCompetitionConfiguration read, UpdateCompetitionConfiguration update, ICompetitionModerationAuthorizer authorizer)
{
    public async Task<LiveSoloConfigurationChange> SaveAsync(Guid competitionId, Guid actorId, LiveSoloCompetitionModeConfiguration configuration,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!await authorizer.CanModerateAsync(actorId, competitionId, ct)) return new(null, LiveSoloFailure.Forbidden);
        if ((await read.ExecuteAsync(competitionId, ct))?.Configuration is not LiveSoloCompetitionModeConfiguration) return new(null, LiveSoloFailure.NotFound);
        var result = await update.ExecuteAsync(competitionId, configuration, now, ct);
        return result.Value?.Configuration is LiveSoloCompetitionModeConfiguration saved ? new(saved)
            : new(null, result.FailureCode switch {
                CompetitionConfigurationFailureCode.CompetitionNotFound => LiveSoloFailure.NotFound,
                CompetitionConfigurationFailureCode.InvalidConfiguration => LiveSoloFailure.InvalidConfiguration,
                _ => LiveSoloFailure.Conflict });
    }
}

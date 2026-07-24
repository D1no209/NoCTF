using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed class GameModeSubmissionAdmissionPolicy : ISubmissionAdmissionModePolicy
{
    public SubmissionAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson) => mode switch
        {
            GameMode.Ctf => CtfRules(challengeConfigurationJson),
            GameMode.Awd => AwdRules(challengeConfigurationJson),
            GameMode.Awdp => AwdpRules(
                competitionConfigurationJson,
                challengeConfigurationJson),
            GameMode.Koh => new(false, false, null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
        };

    private static SubmissionAdmissionRules CtfRules(string json)
    {
        var configuration = Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, configuration.MaxFlagAttempts, null);
    }

    private static SubmissionAdmissionRules AwdRules(string json)
    {
        _ = Awd.Configuration.AwdConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, null, null);
    }

    private static SubmissionAdmissionRules AwdpRules(
        string competitionJson,
        string challengeJson)
    {
        var competition = Awdp.Configuration.AwdpConfigurationParser.ParseCompetition(
            competitionJson);
        var challenge = Awdp.Configuration.AwdpConfigurationParser.ParseChallenge(
            challengeJson);
        return new(
            challenge.Break is not null,
            challenge.Fix is not null,
            challenge.Break is null ? null : challenge.MaxBreakAttempts,
            challenge.Fix is null ? null : challenge.MaxFixAttempts,
            challenge.RequireBreakBeforeFix ?? competition.RequireBreakBeforeFix);
    }
}

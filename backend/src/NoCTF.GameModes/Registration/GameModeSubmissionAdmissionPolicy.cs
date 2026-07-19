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
            GameMode.Awdp => AwdpRules(challengeConfigurationJson),
            GameMode.Koh => new(false, false, null, null),
            GameMode.Penetration => PenetrationRules(challengeConfigurationJson),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.")
        };

    private static SubmissionAdmissionRules CtfRules(string json)
    {
        var configuration = Ctf.Configuration.CtfConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, configuration.MaxFlagAttempts, null);
    }

    private static SubmissionAdmissionRules AwdRules(string json)
    {
        var configuration = Awd.Configuration.AwdConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, configuration.MaxFlagAttempts, null);
    }

    private static SubmissionAdmissionRules AwdpRules(string json)
    {
        var configuration = Awdp.Configuration.AwdpConfigurationUpgrader.ParseChallenge(json);
        return new(
            configuration.Break is not null,
            configuration.Fix is not null,
            configuration.Break is null ? null : configuration.MaxBreakAttempts,
            configuration.Fix is null ? null : configuration.MaxFixAttempts);
    }

    private static SubmissionAdmissionRules PenetrationRules(string json)
    {
        var configuration = Penetration.Configuration.PenetrationConfigurationUpgrader.ParseChallenge(json);
        return new(true, false, configuration.MaxFlagAttempts, null);
    }
}

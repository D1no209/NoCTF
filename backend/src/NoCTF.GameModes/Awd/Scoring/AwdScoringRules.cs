using NoCTF.Application.Submissions.Events;

namespace NoCTF.GameModes.Awd.Scoring;

public static class AwdScoringRules
{
    public static bool IsPenalized(AwdServiceObservation observation) =>
        observation == AwdServiceObservation.Down;

    public static bool IsNeutral(AwdServiceObservation observation) =>
        observation == AwdServiceObservation.PlatformError;
}

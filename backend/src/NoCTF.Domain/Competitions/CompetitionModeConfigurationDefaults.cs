namespace NoCTF.Domain.Competitions;

public static class CompetitionModeConfigurationDefaults
{
    public static CompetitionModeConfiguration Create(GameMode mode, Guid competitionId)
    {
        CompetitionModeConfiguration configuration = mode switch
        {
            GameMode.Ctf => new CtfCompetitionModeConfiguration
            {
                DefaultScoreCurve = Curve(500, 100, 10)
            },
            GameMode.Awd => new AwdCompetitionModeConfiguration
            {
                HardeningDurationSeconds = 0,
                RoundDurationSeconds = 300,
                AttackRewardMode = AwdAttackRewardMode.FixedPerAttack,
                AttackPoints = 50,
                VictimDefensePoolPoints = 100,
                CheckerIntervalSeconds = 30,
                ServiceHealthyPoints = 100,
                ServiceUnhealthyPenalty = 50
            },
            GameMode.Awdp => new AwdpCompetitionModeConfiguration
            {
                RoundDurationSeconds = 300,
                BreakScoreCurve = Curve(500, 100, 10),
                FixScoreCurve = Curve(500, 100, 10),
                RequireBreakBeforeFix = true,
                MaxBreakSubmissions = 10,
                MaxFixSubmissions = 10,
                EvaluationDispatchMode = CompetitionEvaluationDispatchMode.Automatic
            },
            GameMode.Koh => new KohCompetitionModeConfiguration
            {
                PollIntervalSeconds = 5,
                ControlPointsPerInterval = 10
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        configuration.CompetitionId = competitionId;
        configuration.FlagTemplate = new FlagTemplateValue();
        return configuration;
    }

    private static ScoreCurveValue Curve(long initial, long minimum, int teams) => new()
    {
        InitialPoints = initial,
        MinimumPoints = minimum,
        DecayTeamCount = teams,
        DecayMode = PersistedScoreDecayMode.Quadratic
    };
}

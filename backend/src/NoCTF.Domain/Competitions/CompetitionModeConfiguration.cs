using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions;

[PersistentHierarchy]
public abstract class CompetitionModeConfiguration
{
    protected CompetitionModeConfiguration(GameMode mode) => Mode = mode;

    public Guid CompetitionId { get; set; }
    public GameMode Mode { get; private set; }
    public FlagTemplateValue FlagTemplate { get; set; } = new();
}

public sealed class ScoreCurveValue
{
    public long InitialPoints { get; set; }
    public long MinimumPoints { get; set; }
    public int DecayTeamCount { get; set; }
    public PersistedScoreDecayMode DecayMode { get; set; }
    public string? CustomExpression { get; set; }
}

public enum PersistedScoreDecayMode : short
{
    Fixed,
    Linear,
    Quadratic,
    Exponential,
    Logarithmic,
    Custom
}

public sealed class FlagTemplateValue
{
    public string Header { get; set; } = "flag";
    public string BodyTemplate { get; set; } = "[GUID]";
    public bool LeetLiteralText { get; set; }
}

public enum CompetitionBloodRewardPolicy : short
{
    FixedPoints,
    InitialPointsPercentage,
    SolveTimePointsPercentage,
    CurrentPointsPercentage
}

public sealed class CompetitionBloodReward
{
    public Guid CompetitionId { get; set; }
    public int Position { get; set; }
    public CompetitionBloodRewardPolicy Policy { get; set; }
    public decimal Value { get; set; }
}

[PersistentDiscriminator("ctf")]
public sealed class CtfCompetitionModeConfiguration()
    : CompetitionModeConfiguration(GameMode.Ctf)
{
    public ScoreCurveValue DefaultScoreCurve { get; set; } = new();
    public List<CompetitionBloodReward> BloodRewards { get; set; } = [];
    public long WrongSubmissionPenalty { get; set; }
}

public enum AwdAttackRewardMode : short
{
    FixedPerAttack,
    SplitVictimDefensePool
}

[PersistentDiscriminator("awd")]
public sealed class AwdCompetitionModeConfiguration()
    : CompetitionModeConfiguration(GameMode.Awd)
{
    public int HardeningDurationSeconds { get; set; }
    public int RoundDurationSeconds { get; set; }
    public AwdAttackRewardMode AttackRewardMode { get; set; }
    public long AttackPoints { get; set; }
    public long VictimDefensePoolPoints { get; set; }
    public int CheckerIntervalSeconds { get; set; }
    public long ServiceHealthyPoints { get; set; }
    public long ServiceUnhealthyPenalty { get; set; }
}

public enum CompetitionEvaluationDispatchMode : short
{
    Automatic,
    Manual
}

[PersistentDiscriminator("awdp")]
public sealed class AwdpCompetitionModeConfiguration()
    : CompetitionModeConfiguration(GameMode.Awdp)
{
    public int RoundDurationSeconds { get; set; }
    public ScoreCurveValue BreakScoreCurve { get; set; } = new();
    public ScoreCurveValue FixScoreCurve { get; set; } = new();
    public long FlagWrongPenalty { get; set; }
    public long ExploitSucceededPenalty { get; set; }
    public long ServiceAbnormalPenalty { get; set; }
    public bool RequireBreakBeforeFix { get; set; }
    public int MaxBreakSubmissions { get; set; }
    public int MaxFixSubmissions { get; set; }
    public CompetitionEvaluationDispatchMode EvaluationDispatchMode { get; set; }
}

[PersistentDiscriminator("koh")]
public sealed class KohCompetitionModeConfiguration()
    : CompetitionModeConfiguration(GameMode.Koh)
{
    public int PollIntervalSeconds { get; set; }
    public long ControlPointsPerInterval { get; set; }
}

using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloBracketFormat : short { SingleElimination, DoubleElimination }
public enum LiveSoloBracketLane : short { Winners, Losers, GrandFinal, ResetFinal }

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloCompetitionModeConfiguration() : CompetitionModeConfiguration(GameMode.LiveSolo)
{
    public bool Enabled { get; set; }
    public bool PlatformStreamingEnabled { get; set; } = true;
    public LiveSoloBracketFormat BracketFormat { get; set; }
    public int RequiredWins { get; set; } = 2;
    public int CountdownSeconds { get; set; } = 5;
    public int QuestionIntervalSeconds { get; set; } = 180;
    public int RoundLimitSeconds { get; set; } = 900;
    public int PublicDelaySeconds { get; set; } = 60;
    public bool ParticipantsMayViewOpponents { get; set; }
    public bool RecordingEnabled { get; set; }
    public int RecordingRetentionDays { get; set; } = 30;
    public int MaximumConcurrentMatches { get; set; } = 4;
    public int MaximumRosterMembers { get; set; } = 2;
    public int MaximumViewers { get; set; } = 50;
    public List<LiveSoloStageRule> StageRules { get; set; } = [];
}

public sealed class LiveSoloStageRule
{
    public Guid CompetitionId { get; set; }
    public LiveSoloBracketLane Lane { get; set; }
    public int Stage { get; set; }
    public int RequiredWins { get; set; }
}

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloChallengeDefinition() : ChallengeDefinition(GameMode.LiveSolo);

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.LiveSolo);

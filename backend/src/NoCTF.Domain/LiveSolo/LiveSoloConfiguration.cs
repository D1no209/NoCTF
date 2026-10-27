using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloBracketFormat : short { SingleElimination, DoubleElimination, Swiss }
public enum LiveSoloBracketLane : short { Winners, Losers, GrandFinal, ResetFinal, Swiss }

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloCompetitionModeConfiguration() : CompetitionModeConfiguration(GameMode.LiveSolo)
{
    public bool Enabled { get; set; }
    public bool StaticFlagEvidenceRequired { get; set; }
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
    public List<LiveSoloTournamentStage> TournamentStages { get; set; } = [];
}

public sealed class LiveSoloTournamentStage
{
    public Guid CompetitionId { get; set; }
    public int Position { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(120)] public string Name { get; set; } = "";
    public LiveSoloBracketFormat Format { get; set; }
    public int RequiredWins { get; set; } = 1;
    public int AdvanceCount { get; set; } = 1;
    public int SwissAdvanceWins { get; set; } = 3;
    public int SwissEliminateLosses { get; set; } = 3;
    public int SwissQualificationRequiredWins { get; set; } = 2;
    public int SwissEliminationRequiredWins { get; set; } = 2;
}

public sealed class LiveSoloStageRule
{
    public Guid CompetitionId { get; set; }
    public LiveSoloBracketLane Lane { get; set; }
    public int Stage { get; set; }
    public int RequiredWins { get; set; }
    public int TournamentStage { get; set; } = 1;
}

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloChallengeDefinition() : ChallengeDefinition(GameMode.LiveSolo);

[PersistentDiscriminator("livesolo")]
public sealed class LiveSoloCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.LiveSolo);

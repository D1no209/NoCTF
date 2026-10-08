namespace NoCTF.Domain.Competitions;

/// <summary>Defines the persisted lifecycle state of a competition.</summary>
public enum CompetitionStatus
{
    Draft,
    Visible,
    Published,
    Running,
    Paused,
    Finished
}

/// <summary>Identifies a compile-time game mode.</summary>
public enum GameMode
{
    Ctf,
    Awd,
    Awdp,
    Koh,
    LiveSolo
}

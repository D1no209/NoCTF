namespace NoCTF.Domain.Competitions;

/// <summary>Defines the persisted lifecycle state of a competition.</summary>
public enum CompetitionStatus
{
    Draft,
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
    Penetration
}

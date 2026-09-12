namespace NoCTF.Domain.Competitions;

/// <summary>Controls who may discover and access a competition.</summary>
public enum CompetitionAccessMode : short
{
    Public,
    StaffOnly
}

/// <summary>Identifies why the effective competition audience changed.</summary>
public enum CompetitionAudienceChangeKind : short
{
    AccessMode,
    Collaborators,
    Owner
}

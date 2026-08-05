namespace NoCTF.Domain.Teams;

public enum TeamBanSource : short
{
    ManualModeration,
    CheatIncident
}

public enum TeamBanAppealStatus : short
{
    Submitted,
    Upheld,
    Accepted
}

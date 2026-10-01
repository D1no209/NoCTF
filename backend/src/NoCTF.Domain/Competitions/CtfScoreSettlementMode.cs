namespace NoCTF.Domain.Competitions;

/// <summary>Determines whether later completions reprice earlier CTF solves.</summary>
public enum CtfScoreSettlementMode : short
{
    DynamicRecalculation,
    AtSolve
}

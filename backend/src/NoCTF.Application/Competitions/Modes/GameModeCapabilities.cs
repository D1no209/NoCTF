using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Modes;

public sealed record GameModeCapabilities(bool OrdinaryPlayerChallengeAccess, bool OrdinaryScoreboard,
    bool BloodAwards, bool PaidHints, bool WriteUpBenefitDiscount, bool ScopedExecution);

/// <summary>One explicit mode boundary for capabilities that would otherwise be scattered through feature code.</summary>
public static class CompetitionModeCapabilities
{
    public static GameModeCapabilities For(GameMode mode) => mode switch
    {
        GameMode.Ctf => new(true, true, true, true, true, false),
        GameMode.Awd or GameMode.Awdp or GameMode.Koh => new(true, true, false, true, true, false),
        GameMode.LiveSolo => new(false, false, false, false, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

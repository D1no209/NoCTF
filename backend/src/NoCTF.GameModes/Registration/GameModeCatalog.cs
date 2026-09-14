using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

public sealed record GameModeDescriptor(GameMode Mode, int CurrentSchemaVersion, bool SupportsFixAttempts);

/// <summary>Describes the four compile-time modes without runtime plugin discovery.</summary>
public static class GameModeCatalog
{
    public static IReadOnlyList<GameModeDescriptor> All { get; } =
    [
        new(GameMode.Ctf, Ctf.Configuration.CtfConfiguration.CurrentSchemaVersion, true),
        new(GameMode.Awd, Awd.Configuration.AwdConfiguration.CurrentSchemaVersion, false),
        new(GameMode.Awdp, Awdp.Configuration.AwdpConfiguration.CurrentSchemaVersion, true),
        new(GameMode.Koh, Koh.Configuration.KohConfiguration.CurrentSchemaVersion, false)
    ];

    public static GameModeDescriptor Get(GameMode mode) =>
        All.Single(descriptor => descriptor.Mode == mode);
}

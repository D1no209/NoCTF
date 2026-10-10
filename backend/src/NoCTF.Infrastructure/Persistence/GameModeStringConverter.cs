using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence;

internal sealed class GameModeStringConverter : ValueConverter<GameMode, string>
{
    public GameModeStringConverter() : base(
        mode => ToDiscriminator(mode),
        value => FromDiscriminator(value))
    {
    }

    private static string ToDiscriminator(GameMode mode) => mode switch
    {
        GameMode.Ctf => "ctf",
        GameMode.Awd => "awd",
        GameMode.Awdp => "awdp",
        GameMode.Koh => "koh",
        GameMode.LiveSolo => "livesolo",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static GameMode FromDiscriminator(string value) => value switch
    {
        "ctf" => GameMode.Ctf,
        "awd" => GameMode.Awd,
        "awdp" => GameMode.Awdp,
        "koh" => GameMode.Koh,
        "livesolo" => GameMode.LiveSolo,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}

namespace NoCTF.Bot.Commands;

public enum BotCommandKind
{
    Help,
    Status,
    Challenges,
    Rank,
    Team,
    Link,
    Subscribe,
    Unsubscribe,
    Broadcasts,
    Scoreboard,
    Blood,
    Config,
    Invalid
}

public sealed record BotCommand(
    BotCommandKind Kind,
    string? Argument = null,
    int RankLimit = 10,
    bool? Enabled = null);

internal static class BotCommandParser
{
    public static BotCommand? Parse(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/ctf", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Length > 4 && !char.IsWhiteSpace(trimmed[4])) return null;
        var remainder = trimmed.Length == 4 ? string.Empty : trimmed[4..].Trim();
        if (remainder.Length == 0) return new(BotCommandKind.Help);
        var separator = remainder.IndexOfAny([' ', '\t', '\r', '\n']);
        var name = separator < 0 ? remainder : remainder[..separator];
        var argument = separator < 0 ? string.Empty : remainder[(separator + 1)..].Trim();
        return name.ToLowerInvariant() switch
        {
            "help" => new(BotCommandKind.Help),
            "status" when argument.Length == 0 => new(BotCommandKind.Status),
            "challenges" when argument.Length == 0 => new(BotCommandKind.Challenges),
            "rank" => ParseRank(argument),
            "team" when argument.Length > 0 => new(BotCommandKind.Team, argument),
            "link" when argument.Length == 0 => new(BotCommandKind.Link),
            "subscribe" when Guid.TryParse(argument, out _) => new(BotCommandKind.Subscribe, argument),
            "unsubscribe" when argument.Length == 0 => new(BotCommandKind.Unsubscribe),
            "broadcasts" => ParseToggle(BotCommandKind.Broadcasts, argument),
            "scoreboard" => ParseToggle(BotCommandKind.Scoreboard, argument),
            "blood" => ParseToggle(BotCommandKind.Blood, argument),
            "config" when argument.Length == 0 => new(BotCommandKind.Config),
            _ => new(BotCommandKind.Invalid)
        };
    }

    private static BotCommand ParseRank(string argument)
    {
        if (argument.Length == 0) return new(BotCommandKind.Rank);
        return int.TryParse(argument, out var limit) && limit is >= 1 and <= 20
            ? new(BotCommandKind.Rank, RankLimit: limit)
            : new(BotCommandKind.Invalid);
    }

    private static BotCommand ParseToggle(BotCommandKind kind, string argument) =>
        argument.ToLowerInvariant() switch
        {
            "on" => new(kind, Enabled: true),
            "off" => new(kind, Enabled: false),
            _ => new(BotCommandKind.Invalid)
        };
}

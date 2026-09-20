using NoCTF.Bot.Commands;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotCommandParserTests
{
    [Test]
    [Arguments("/ctf", BotCommandKind.Help)]
    [Arguments("/ctf status", BotCommandKind.Status)]
    [Arguments("/CTF challenges", BotCommandKind.Challenges)]
    [Arguments("/ctf rank 20", BotCommandKind.Rank)]
    [Arguments("/ctf team Red Team", BotCommandKind.Team)]
    [Arguments("/ctf broadcasts off", BotCommandKind.Broadcasts)]
    [Arguments("enable", BotCommandKind.Enable)]
    [Arguments("bind 01a041a5-d3b7-754b-b399-8106afe5be14", BotCommandKind.Bind)]
    [Arguments("/ctf admin add 30003", BotCommandKind.AdminAdd)]
    [Arguments("/ctf admins", BotCommandKind.AdminList)]
    public async Task Parse_ValidCommand_ReturnsExpectedKind(
        string text,
        BotCommandKind expected)
    {
        var command = BotCommandParser.Parse(text);

        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments("hello")]
    [Arguments("/ctf-not-a-command")]
    public async Task Parse_NonCommand_ReturnsNull(string text)
    {
        await Assert.That(BotCommandParser.Parse(text)).IsNull();
    }

    [Test]
    [Arguments("/ctf rank 21")]
    [Arguments("/ctf subscribe nope")]
    [Arguments("/ctf broadcasts maybe")]
    [Arguments("/ctf flag test")]
    [Arguments("bind nope")]
    public async Task Parse_UnsafeOrInvalidCommand_IsRejected(string text)
    {
        var command = BotCommandParser.Parse(text);

        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Kind).IsEqualTo(BotCommandKind.Invalid);
    }

    [Test]
    [Arguments("/ctf rank", null, 10)]
    [Arguments("/ctf rank 20", null, 20)]
    [Arguments("/ctf rank open", "open", 10)]
    [Arguments("/ctf rank open 20", "open", 20)]
    public async Task Parse_RankCommand_ReturnsTrackAndLimit(
        string text,
        string? expectedTrack,
        int expectedLimit)
    {
        var command = BotCommandParser.Parse(text);

        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Kind).IsEqualTo(BotCommandKind.Rank);
        await Assert.That(command.Argument).IsEqualTo(expectedTrack);
        await Assert.That(command.RankLimit).IsEqualTo(expectedLimit);
    }

    [Test]
    [Arguments("/ctf rank open 21")]
    [Arguments("/ctf rank open 10 extra")]
    public async Task Parse_InvalidRankArguments_IsRejected(string text)
    {
        var command = BotCommandParser.Parse(text);

        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Kind).IsEqualTo(BotCommandKind.Invalid);
    }
}

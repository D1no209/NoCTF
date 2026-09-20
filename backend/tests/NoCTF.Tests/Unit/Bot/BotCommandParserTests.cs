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
    public async Task Parse_UnsafeOrInvalidCommand_IsRejected(string text)
    {
        var command = BotCommandParser.Parse(text);

        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Kind).IsEqualTo(BotCommandKind.Invalid);
    }
}

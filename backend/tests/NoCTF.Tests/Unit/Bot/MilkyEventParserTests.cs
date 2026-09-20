using NoCTF.Bot.Providers.Milky;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class MilkyEventParserTests
{
    [Test]
    public async Task ParseGroupMessage_TextSegments_ReturnsStableMilkyIdentity()
    {
        const string json = """
            {
              "time": 1760000000,
              "self_id": 10001,
              "event_type": "message_receive",
              "data": {
                "message_scene": "group",
                "peer_id": 20002,
                "sender_id": 30003,
                "message_seq": 42,
                "segments": [
                  { "type": "mention", "data": { "user_id": 10001 } },
                  { "type": "text", "data": { "text": " /ctf " } },
                  { "type": "text", "data": { "text": "rank 20" } }
                ]
              }
            }
            """;

        var message = MilkyEventParser.ParseGroupMessage(json);

        await Assert.That(message).IsNotNull();
        await Assert.That(message!.ProviderId).IsEqualTo("milky");
        await Assert.That(message.GroupId).IsEqualTo("20002");
        await Assert.That(message.SenderId).IsEqualTo("30003");
        await Assert.That(message.MessageId).IsEqualTo("42");
        await Assert.That(message.Text).IsEqualTo("/ctf rank 20");
    }

    [Test]
    public async Task ParseGroupMessage_PrivateMessage_IsIgnored()
    {
        const string json = """
            {
              "time": 1760000000,
              "self_id": 10001,
              "event_type": "message_receive",
              "data": {
                "message_scene": "friend",
                "peer_id": 30003,
                "sender_id": 30003,
                "message_seq": 42,
                "segments": [{ "type": "text", "data": { "text": "/ctf status" } }]
              }
            }
            """;

        await Assert.That(MilkyEventParser.ParseGroupMessage(json)).IsNull();
    }

    [Test]
    public async Task ParseGroupMessage_MessageFromBotItself_IsIgnored()
    {
        const string json = """
            {
              "time": 1760000000,
              "self_id": 10001,
              "event_type": "message_receive",
              "data": {
                "message_scene": "group",
                "peer_id": 20002,
                "sender_id": 10001,
                "message_seq": 43,
                "segments": [{ "type": "text", "data": { "text": "/ctf status" } }]
              }
            }
            """;

        await Assert.That(MilkyEventParser.ParseGroupMessage(json)).IsNull();
    }
}

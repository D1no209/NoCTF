using Microsoft.Extensions.Options;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Providers;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class ChatProviderTests
{
    [Test]
    public async Task Catalog_SelectsConfiguredProviderAndNormalizesMaster()
    {
        var provider = new FakeProvider("fake");
        var catalog = new ChatProviderCatalog(
            [provider],
            Options.Create(new RelayOptions
            {
                Provider = "FAKE",
                MasterUserId = " Master "
            }));

        await Assert.That(catalog.Active).IsSameReferenceAs(provider);
        await Assert.That(catalog.MasterUserId).IsEqualTo("master");
    }

    [Test]
    public async Task Catalog_DuplicateProviderIdentifiers_FailsFast()
    {
        void Create() => _ = new ChatProviderCatalog(
            [new FakeProvider("fake"), new FakeProvider("FAKE")],
            Options.Create(new RelayOptions
            {
                Provider = "fake",
                MasterUserId = "master"
            }));

        await Assert.That(Create).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MessageText_LongAnnouncement_SplitsWithinProviderLimit()
    {
        var parts = MessageText.Split(new string('a', 600) + "\n" + new string('b', 600), 512);

        await Assert.That(parts.Count).IsEqualTo(4);
        await Assert.That(parts.All(part => part.Length <= 512)).IsTrue();
    }

    private sealed class FakeProvider(string id) : IChatProvider
    {
        public string Id { get; } = id;
        public ChatProviderCapabilities Capabilities { get; } = new(true, true, 512);
        public bool TryNormalizeUserId(string value, out string normalized)
        {
            normalized = value.Trim().ToLowerInvariant();
            return normalized.Length > 0;
        }
        public Task<ChatProviderIdentity> GetIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ChatProviderIdentity("bot", "Bot"));
        public async IAsyncEnumerable<ChatGroupMessage> ReadGroupMessagesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<ChatGroupRole> GetGroupMemberRoleAsync(
            string groupId,
            string userId,
            CancellationToken cancellationToken) => Task.FromResult(ChatGroupRole.Member);
        public Task<ChatDeliveryReceipt> SendGroupTextAsync(
            string groupId,
            string text,
            CancellationToken cancellationToken) => Task.FromResult(new ChatDeliveryReceipt("1"));
    }
}

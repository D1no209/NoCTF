using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Bot.Broadcasting;
using NoCTF.Bot.Commands;
using NoCTF.Bot.Configuration;
using NoCTF.Bot.Hosting;
using NoCTF.Bot.NoCtf;
using NoCTF.Bot.Persistence;
using NoCTF.Bot.Providers;

namespace NoCTF.Tests.Unit.Bot;

[Category("Bot")]
public sealed class BotCommandPermissionTests
{
    [Test]
    public async Task Commands_RequireMasterFirstEnableAndKeepCustomAdminsGroupScoped()
    {
        var directory = Directory.CreateTempSubdirectory("noctf-bot-permissions-");
        try
        {
            var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-20T00:00:00Z"));
            var options = Options.Create(new RelayOptions
            {
                StatePath = Path.Combine(directory.FullName, "state.sqlite3"),
                Provider = "fake",
                MasterUserId = "10001"
            });
            using var store = new BotStateStore(options, time);
            store.Initialize();
            var provider = new PermissionProvider();
            var catalog = new ChatProviderCatalog([provider], options);
            var outbound = new OutboundMessageQueue(store, catalog);
            var noCtfOptions = Options.Create(new NoCtfBotOptions
            {
                BaseUrl = new("https://noctf.test"),
                PublicBaseUrl = new("https://noctf.test"),
                AccessToken = "test"
            });
            var processor = new BotCommandProcessor(
                store,
                new NoCtfClient(new HttpClient(new RejectingHandler()), noCtfOptions, time),
                new BroadcastMessageFormatter(options, time),
                new RefreshScheduler(),
                new SubscriptionMonitor(),
                new BotRuntimeState(),
                catalog,
                outbound,
                noCtfOptions,
                options,
                time,
                new SlidingWindowLimiter(time),
                NullLogger<BotCommandProcessor>.Instance);

            await processor.ProcessAsync(Message("group-a", "20002", "1", "enable"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-a")).IsNull();

            await processor.ProcessAsync(Message("group-a", "10001", "2", "enable"), CancellationToken.None);
            await processor.ProcessAsync(Message("group-a", "10001", "3", "/ctf admin add 20002"), CancellationToken.None);
            await processor.ProcessAsync(Message("group-a", "20002", "4", "disable"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-a")!.Enabled).IsFalse();

            await processor.ProcessAsync(Message("group-a", "20002", "5", "enable"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-a")!.Enabled).IsTrue();

            await processor.ProcessAsync(Message("group-b", "10001", "6", "enable"), CancellationToken.None);
            await processor.ProcessAsync(Message("group-b", "20002", "7", "disable"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-b")!.Enabled).IsTrue();

            await processor.ProcessAsync(Message("group-b", "30003", "native-admin", "disable"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-b")!.Enabled).IsFalse();

            await processor.ProcessAsync(Message("group-a", "10001", "8", "/ctf revoke"), CancellationToken.None);
            await Assert.That(store.GetGroupAccess("fake", "group-a")).IsNull();
            await Assert.That(store.IsGroupAdmin("fake", "group-a", "20002")).IsFalse();
            store.Dispose();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ChatGroupMessage Message(
        string groupId,
        string senderId,
        string messageId,
        string text) => new("fake", groupId, senderId, messageId, text);

    private sealed class PermissionProvider : IChatProvider
    {
        public string Id => "fake";
        public ChatProviderCapabilities Capabilities { get; } = new(true, true, 512);
        public bool TryNormalizeUserId(string value, out string normalized)
        {
            normalized = value.Trim();
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
            CancellationToken cancellationToken) => Task.FromResult(
                userId == "30003" ? ChatGroupRole.Admin : ChatGroupRole.Member);
        public Task<ChatDeliveryReceipt> SendGroupTextAsync(
            string groupId,
            string text,
            CancellationToken cancellationToken) => Task.FromResult(new ChatDeliveryReceipt("1"));
    }

    private sealed class RefreshScheduler : ICompetitionRefreshScheduler
    {
        public void Schedule(
            Guid competitionId,
            CompetitionRefreshReason reason,
            CompetitionSignal? signal = null,
            TimeSpan? delay = null)
        {
        }
    }

    private sealed class SubscriptionMonitor : ICompetitionSubscriptionMonitor
    {
        public void SignalChanged()
        {
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

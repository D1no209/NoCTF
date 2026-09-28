using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Notifications;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;

namespace NoCTF.Tests.Unit.API;

public sealed class NotificationFeedEndpointTests
{
    private static readonly Guid ActorId =
        Guid.Parse("019bf9b5-e4cc-711a-b231-562e32ad7286");
    private const string SigningKey =
        "notification-feed-tests-use-a-stable-32-byte-signing-key";

    [Test]
    public async Task First_request_returns_empty_checkpoint_then_reads_ascending_events()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var reader = new RecordingReader(new(now, Guid.CreateVersion7(now)));
        await using var app = await CreateApplicationAsync(reader);
        using var client = app.GetTestClient();

        using var initialResponse = await client.GetAsync(
            "/api/v1/notifications/feed?limit=2");
        await Assert.That(initialResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var initial = await initialResponse.Content.ReadFromJsonAsync<NotificationFeedResponse>();
        await Assert.That(initial).IsNotNull();
        await Assert.That(initial!.Items).IsEmpty();
        await Assert.That(initial.NextCursor).IsNotNull();
        await Assert.That(initial.NextCursor).IsNotEmpty();
        await Assert.That(reader.ReadCalls).IsEqualTo(0);

        var firstId = Guid.CreateVersion7(now.AddSeconds(1));
        var secondId = Guid.CreateVersion7(now.AddSeconds(2));
        reader.Items =
        [
            new(firstId, NotificationSourceType.System, null,
                NotificationTargetType.User, ActorId, NotificationKind.ChallengePublished,
                new ChallengePublishedNotificationContent(
                    null, null, null, null, null),
                null, null, null, null, now.AddSeconds(1)),
            new(secondId, NotificationSourceType.System, null,
                NotificationTargetType.User, ActorId, NotificationKind.HintPublished,
                new HintPublishedNotificationContent(
                    null, null, null, null, null, null),
                null, null, null, null, now.AddSeconds(2))
        ];
        using var feedResponse = await client.GetAsync(
            $"/api/v1/notifications/feed?limit=2&cursor={Uri.EscapeDataString(initial.NextCursor)}");
        var feed = await feedResponse.Content.ReadFromJsonAsync<NotificationFeedResponse>();

        await Assert.That(feedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(feed).IsNotNull();
        await Assert.That(feed!.Items.Select(item => item.Id))
            .IsEquivalentTo([firstId, secondId]);
        await Assert.That(reader.ReadCalls).IsEqualTo(1);
        await Assert.That(reader.LastUserId).IsEqualTo(ActorId);
    }

    [Test]
    public async Task Cursor_from_another_identity_is_rejected()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var reader = new RecordingReader(new(now, Guid.Empty));
        await using var app = await CreateApplicationAsync(reader);
        using var client = app.GetTestClient();
        var cursor = new SignedKeysetCursor(Options.Create(new PaginationOptions
                {
                    SigningKey = SigningKey
                }))
            .Encode(
                "notifications.feed",
                Guid.CreateVersion7().ToString("N"),
                new(now, Guid.Empty));

        using var response = await client.GetAsync(
            $"/api/v1/notifications/feed?cursor={Uri.EscapeDataString(cursor)}");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("CursorInvalid");
        await Assert.That(reader.ReadCalls).IsEqualTo(0);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        RecordingReader reader)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:SigningKey"] = SigningKey;
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ReadNotificationFeedEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(ReadNotificationFeedEndpoint)
                || type == typeof(ReadNotificationFeedValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = "Bearer";
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(
                "Bearer",
                _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<INotificationReader>(reader);
        builder.Services.AddScoped<ReadNotificationFeed>();
        builder.Services.AddSingleton<SignedKeysetCursor>();
        builder.Services.AddSingleton<IUserContext>(new ActorUserContext());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class RecordingReader(KeysetNotificationPosition checkpoint)
        : INotificationReader
    {
        public IReadOnlyList<NotificationView> Items { get; set; } = [];
        public int ReadCalls { get; private set; }
        public Guid? LastUserId { get; private set; }

        public Task<IReadOnlyList<NotificationView>> ListAsync(
            Guid userId,
            DateTimeOffset? beforeCreatedAt,
            Guid? beforeId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationView>>([]);

        public Task<KeysetNotificationPosition> GetFeedCheckpointAsync(
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            LastUserId = userId;
            return Task.FromResult(checkpoint);
        }

        public Task<IReadOnlyList<NotificationView>> ReadFeedAsync(
            Guid userId,
            KeysetNotificationPosition position,
            int limit,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            LastUserId = userId;
            return Task.FromResult<IReadOnlyList<NotificationView>>(
                Items.Take(limit).ToArray());
        }
    }

    private sealed class ActorUserContext : IUserContext
    {
        public Guid UserId => ActorId;
        public bool IsAdministrator => false;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

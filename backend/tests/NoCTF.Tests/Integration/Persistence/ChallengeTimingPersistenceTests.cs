using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Common;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.GameModes.Registration;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Challenges.Timing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Notifications;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Domain.Teams;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTimingPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Http_timing_preview_requires_management_and_patch_preserves_omitted_and_clears_null(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var access = Substitute.For<ICompetitionModerationAuthorizer>();
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(true);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PreviewChallengeTimingEndpoint).Assembly];
            options.Filter = type => type == typeof(PreviewChallengeTimingEndpoint) || type == typeof(PatchCompetitionChallengeEndpoint);
        });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IUserContext>(new TestUserContext(f.Competition.OwnerId));
        builder.Services.AddSingleton(access);
        var messages = Substitute.For<IPostCommitMessagePublisher>();
        builder.Services.AddSingleton<IChallengeManagementStore>(f.Management);
        builder.Services.AddSingleton(new GetChallenge(f.Management));
        builder.Services.AddSingleton(new UpdateChallenge(f.Management));
        var configuration = new ChallengeConfigurationStore(f.Db, messages, NullCompetitionEventRecorder.Instance);
        builder.Services.AddSingleton(new GetChallengeConfiguration(configuration));
        builder.Services.AddSingleton(new UpdateChallengeConfiguration(configuration, new GameModeChallengeConfigurationCatalog()));
        builder.Services.AddSingleton<IAtomicAggregatePatch>(new AggregatePatchTransaction(f.Db, messages));
        builder.Services.AddFusionCache(NoCtfCacheNames.Leaderboards);
        builder.Services.AddSingleton<ILeaderboardSnapshotFactory>(sp => new FusionLeaderboardCache(f.Db,
            new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()), Substitute.For<ILeaderboardRefreshPublisher>(), sp.GetRequiredService<IFusionCacheProvider>()));
        builder.Services.AddSingleton(new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) })));
        builder.Services.AddSingleton<IChallengeTimingPreviewReader, ChallengeTimingPreviewReader>();
        builder.Services.AddSingleton<IChallengeTimingStore>(f.Store);
        builder.Services.AddSingleton<ManageChallengeTiming>();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync(ct);
        using var client = app.GetTestClient();
        var uri = $"/api/v1/admin/competitions/{f.Competition.Id}/challenges/{f.Challenge.Id}";
        using var anonymous = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { } }, ct);
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic");
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(false);
        using var forbidden = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { } }, ct);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        access.CanModerateAsync(f.Competition.OwnerId, f.Competition.Id, Arg.Any<CancellationToken>()).Returns(true);
        using var invalid = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { autoOpenAt = f.Now.AddHours(2), scoringEndsAt = f.Now } }, ct);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(20), f.Now.AddMinutes(30)), f.Now), ct);
        using var preserve = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = f.Now.AddMinutes(10) } }, ct);
        await Assert.That(preserve.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.SubmissionDeadlineAt).IsEqualTo(f.Now.AddMinutes(30));
        f.Db.Add(new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            Value = "synthetic", Result = GameplayFactResult.Correct, State = GameplayFactState.Completed, OccurredAt = f.Now.AddMinutes(-2), UpdatedAt = f.Now });
        await f.Db.SaveChangesAsync(ct);
        using var unconfirmed = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = (DateTimeOffset?)null } }, ct);
        await Assert.That(unconfirmed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var preview = await client.PostAsJsonAsync(uri + "/timing-preview", new { timing = new { scoringEndsAt = (DateTimeOffset?)null } }, ct);
        await Assert.That(preview.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await preview.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("token").GetString();
        using var clear = await client.PatchAsJsonAsync(uri, new { timing = new { scoringEndsAt = (DateTimeOffset?)null }, timingPreviewToken = token }, ct);
        await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var refreshed = await f.Db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().SingleAsync(x => x.Id == f.Challenge.Id, ct);
        await Assert.That(refreshed.ScoringEndsAt).IsNull();
        await Assert.That(refreshed.SubmissionDeadlineAt).IsEqualTo(f.Now.AddMinutes(30));
        using var mixed = await client.PatchAsJsonAsync(uri, new { timing = new { }, presentation = new { order = 0, isPublished = false } }, ct);
        await Assert.That(mixed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    });

    private sealed class TestUserContext(Guid userId) : IUserContext { public Guid UserId => userId; public bool IsAdministrator => true; }
    private sealed class TestBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization.Count == 0
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "synthetic")], Scheme.Name)), Scheme.Name)));
    }

    [Test, Timeout(300_000)]
    public Task Preview_uses_candidate_rules_without_writes_and_confirmation_rejects_changed_data(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, Name = "Preview team", CaptainId = f.Competition.OwnerId,
            MemberIds = [f.Competition.OwnerId], InvitationToken = new string('a', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = f.Now };
        f.Db.Add(team);
        var fact = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id, CompetitionChallengeId = f.Challenge.Id,
            TeamId = team.Id, ActorUserId = f.Competition.OwnerId, OccurredAt = f.Now.AddMinutes(-10), UpdatedAt = f.Now,
            Result = GameplayFactResult.Correct, State = GameplayFactState.Completed, Value = "synthetic" };
        f.Db.Add(fact); await f.Db.SaveChangesAsync(ct);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(f.Db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            Substitute.For<ILeaderboardRefreshPublisher>(), services.GetRequiredService<IFusionCacheProvider>());
        var protector = new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
        var previewReader = new ChallengeTimingPreviewReader(f.Db, cache, protector);
        var candidate = new ChangeChallengeTiming(f.Competition.Id, f.Challenge.Id, new(null, f.Now.AddMinutes(-20)), f.Now);
        var preview = await previewReader.PreviewAsync(candidate, f.Competition.OwnerId, ct);
        await Assert.That(preview).IsNotNull();
        await Assert.That(preview!.AffectedAttempts).IsEqualTo(1);
        await Assert.That(preview.Teams.Single().ScoreBefore).IsGreaterThan(0);
        await Assert.That(preview.Teams.Single().ScoreAfter).IsEqualTo(0);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.ScoringEndsAt).IsNull();
        await Assert.That(await previewReader.ValidateAsync(candidate, f.Competition.OwnerId, preview.Token, ct)).IsTrue();
        await Assert.That(await previewReader.ValidateAsync(candidate, Guid.NewGuid(), preview.Token, ct)).IsFalse();
        fact.UpdatedAt = f.Now.AddSeconds(1); await f.Db.SaveChangesAsync(ct);
        await Assert.That(await previewReader.ValidateAsync(candidate, f.Competition.OwnerId, preview.Token, ct)).IsFalse();
        await f.Store.ChangeAsync(candidate, ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        var restore = candidate with { Timing = new(), Now = f.Now.AddSeconds(2) };
        var restorePreview = await previewReader.PreviewAsync(restore, f.Competition.OwnerId, ct);
        await Assert.That(restorePreview!.Teams.Single().ScoreBefore).IsEqualTo(0);
        await Assert.That(restorePreview.Teams.Single().ScoreAfter).IsGreaterThan(0);
        await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.RightButDue);
    });
    [Test, Timeout(300_000)]
    public Task Opening_waits_for_resume_and_a_canceled_or_stale_plan_cannot_publish(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now, f.Now.AddMinutes(30), f.Now.AddHours(1));
        await Assert.That(await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct)).IsNull();
        var revision = f.Challenge.TimingRevision;
        f.Competition.Status = CompetitionStatus.Paused; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        f.Competition.Status = CompetitionStatus.Running; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsTrue();
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddSeconds(5), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(f.Now);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing with { AutoOpenAt = f.Now.AddMinutes(5) }, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddMinutes(10), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        var schedules = await new ChallengeTimingScheduleSource(f.Db).RebuildAsync(f.Now, ct);
        await Assert.That(schedules.Any(x => x.Message is AdvanceChallengeOpening opening && opening.TimingRevision == f.Challenge.TimingRevision)).IsTrue();
    });

    [Test, Timeout(300_000)]
    public Task Latest_time_rules_reclassify_existing_correct_and_wrong_facts_without_new_attempts_or_checker_execution(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var at = f.Now.AddMinutes(-10);
        var correct = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Correct, Value = "synthetic" };
        var wrong = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong, Value = "wrong" };
        f.Db.AddRange(correct, wrong); await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(-1)), f.Now), ct);
        var stale = f.Challenge.TimingRevision;
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(wrong.Result).IsEqualTo(GameplayFactResult.Wrong);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(1)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.Correct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(at.AddMinutes(1), at.AddMinutes(2)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(wrong.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await f.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
    });

    [Test, Timeout(300_000)]
    public Task Timing_updates_rollback_and_other_presentation_updates_preserve_the_schedule(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now.AddMinutes(5), f.Now.AddMinutes(30), f.Now.AddHours(1));
        await using (var transaction = await f.Db.Database.BeginTransactionAsync(ct))
        { await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct); await transaction.RollbackAsync(ct); }
        f.Db.ChangeTracker.Clear(); await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsNull();
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct);
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now, "renamed"), ct);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(timing.AutoOpenAt);
        await Assert.That(f.Challenge.OpeningState).IsEqualTo(ChallengeOpeningState.Pending);
    });

    private sealed class Fixture(PostgreSqlContainer postgres, NoCtfDbContext db, CtfCompetition competition,
        CompetitionChallenge challenge, DateTimeOffset now) : IAsyncDisposable
    {
        public NoCtfDbContext Db { get; } = db;
        public CtfCompetition Competition { get; } = competition;
        public CompetitionChallenge Challenge { get; } = challenge;
        public DateTimeOffset Now { get; } = now;
        public ChallengeManagementStore Management { get; } = new(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
        public ChallengeTimingStore Store => new(Db, Substitute.For<IPostCommitMessagePublisher>(), Management, NullCompetitionEventRecorder.Instance);
        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); var owner = Guid.NewGuid();
            db.Users.Add(new User { Id = owner, UserName = "timing-owner", Email = "timing@test.invalid", PasswordHash = "test",
                AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
            var competition = new CtfCompetition { Id = Guid.NewGuid(), OwnerId = owner, Title = "Timing",
                Status = CompetitionStatus.Running, StartAt = now.AddHours(-1), EndAt = now.AddHours(1),
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32], CreatedAt = now, UpdatedAt = now };
            db.Add(competition);
            var template = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = owner, Title = "Timing template", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now };
            db.Add(template); await db.SaveChangesAsync(ct);
            var management = new ChallengeManagementStore(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
            var created = await management.CreateAsync(new(null, competition.Id, template.Id, 0, now), TestConfigurations.Rules(GameMode.Ctf), ct);
            var challenge = await db.CompetitionChallenges.SingleAsync(x => x.Id == created.Challenge!.Id, ct);
            return new(postgres, db, competition, challenge, now);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await postgres.DisposeAsync(); }
    }
}

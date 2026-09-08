using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.GameplayFacts.Practice;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.GameplayFacts.Practice;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;
using Testcontainers.PostgreSql;
using NoCTF.Application.Teams.Registration;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Infrastructure.Teams.Registration;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Notifications;
using NoCTF.GameModes.Scoring;
using NoCTF.GameModes.Leaderboard;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[NotInParallel]
public sealed class CompetitionPracticeModePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Generated_practice_team_migration_preserves_existing_formal_team(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260908040028_OptionalPublicGateway", ct);
            var now = DateTimeOffset.UtcNow;
            var user = User(Guid.NewGuid(), "migration-team-owner", now);
            var competition = new Competition { Id = Guid.NewGuid(), OwnerId = user.Id, Title = "Existing competition",
                Mode = GameMode.Ctf, Status = CompetitionStatus.Finished, ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32], StartAt = now.AddHours(-2), EndAt = now.AddHours(-1), CreatedAt = now, UpdatedAt = now };
            db.Users.Add(user); db.Competitions.Add(competition);
            await db.SaveChangesAsync(ct);
            var teamId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO teams (id, competition_id, name, captain_id, member_ids, invitation_token,
                    registration_status, registered_at, track_key, is_locked, is_banned)
                VALUES ({teamId}, {competition.Id}, {"Existing formal team"}, {user.Id}, {new[] { user.Id }},
                    {Guid.NewGuid().ToString("N")}, {(short)TeamRegistrationStatus.Approved}, {now}, {"default"}, false, false)
                """, ct);
            await db.Database.MigrateAsync(ct);
            var team = await db.Teams.AsNoTracking().SingleAsync(ct);
            await Assert.That(team.Id).IsEqualTo(teamId);
            await Assert.That(team.IsPracticeTeam).IsFalse();
            await Assert.That(team.MemberIds).IsEquivalentTo([user.Id]);
            await Assert.That(team.RegistrationStatus).IsEqualTo(TeamRegistrationStatus.Approved);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Late_practice_teams_can_join_and_judge_without_entering_official_rankings_and_can_be_banned(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            var joiner = Guid.NewGuid();
            await using var db = new NoCtfDbContext(options);
            db.Users.Add(User(joiner, "late-practice-member", fixture.Now));
            (await db.Competitions.SingleAsync(ct)).MaxTeamMembers = 3;
            (await db.Challenges.SingleAsync(ct)).DefinitionJson = new GameModeChallengeConfigurationCatalog().GetDefaultDefinitionJson(GameMode.Ctf);
            await db.SaveChangesAsync(ct);
            var outbox = new RecordingOutbox();
            var events = new CompetitionEventStore(db, outbox);
            var registration = new TeamRegistrationStore(db, outbox, eventRecorder: events);
            var created = await new CreateTeam(registration).ExecuteAsync(new(fixture.CompetitionId, fixture.OwnerId,
                "Late practice team", fixture.Now.AddMinutes(1), "default"), ct);
            await Assert.That(created.Succeeded).IsTrue();
            var practiceId = created.Value!.Id;
            await Assert.That(created.Value.IsPracticeTeam).IsTrue();
            await Assert.That(created.Value.RegistrationStatus).IsEqualTo(TeamRegistrationStatus.Approved);
            var practice = await db.Teams.SingleAsync(team => team.Id == practiceId, ct);
            var membership = new TeamMembershipStore(db, outbox, eventRecorder: events);
            // Joining an old formal team after the contest would rewrite historical membership.
            var formal = await db.Teams.SingleAsync(team => team.Id == fixture.TeamId, ct);
            await Assert.That(await membership.JoinByInvitationAsync(fixture.CompetitionId, formal.InvitationToken, joiner, fixture.Now, ct))
                .IsEqualTo(TeamMembershipFailure.MembershipLocked);
            await Assert.That(await membership.JoinByInvitationAsync(fixture.CompetitionId, practice.InvitationToken, joiner, fixture.Now, ct)).IsNull();
            var judgement = await new PracticeFlagJudge(db).JudgeAsync(new(fixture.CompetitionId, fixture.CompetitionChallengeId, joiner, fixture.Flag, fixture.Now.AddMinutes(2)), ct);
            await Assert.That(judgement.Judgement).IsEqualTo(PracticeFlagJudgement.Correct);
            await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            using var cacheServices = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards).Services.BuildServiceProvider();
            var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
                Substitute.For<ILeaderboardRefreshPublisher>(), cacheServices.GetRequiredService<IFusionCacheProvider>());
            var board = await cache.CreateAsync(fixture.CompetitionId, fixture.Now.AddMinutes(3), ct);
            await Assert.That(board).IsNotNull();
            await Assert.That(board!.Entries.Any(entry => entry.TeamId == practiceId)).IsFalse();
            await Assert.That(board.Entries.Any(entry => entry.TeamId == fixture.TeamId)).IsTrue();
            var runtime = Runtime(fixture, RuntimeState.Running);
            runtime.TeamId = practiceId;
            runtime.RunnerId = "practice-runner";
            runtime.ProviderReceiptJson = "{}";
            db.RuntimeInstances.Add(runtime);
            await db.SaveChangesAsync(ct);
            var moderation = new ModerateTeam(new TeamModerationStore(db, outbox, events));
            var command = new TeamModerationCommand(fixture.CompetitionId, practiceId, fixture.OwnerId, true, "Practice abuse", fixture.Now.AddMinutes(4));
            await Assert.That((await moderation.ExecuteAsync(command, ct)).Succeeded).IsTrue();
            await Assert.That((await moderation.ExecuteAsync(command, ct)).Succeeded).IsTrue();
            await Assert.That(outbox.Published.OfType<StopRuntime>().Count(message => message.RuntimeInstanceId == runtime.Id)).IsEqualTo(1);
            await db.Entry(runtime).ReloadAsync(ct);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            var rejected = await new PracticeFlagJudge(db).JudgeAsync(new(fixture.CompetitionId, fixture.CompetitionChallengeId, joiner, fixture.Flag, fixture.Now.AddMinutes(5)), ct);
            await Assert.That(rejected.FailureCode).IsEqualTo(PracticeFlagFailureCode.TeamNotEligible);
            await Assert.That((await moderation.ExecuteAsync(command with { Ban = false }, ct)).Succeeded).IsTrue();
            await Assert.That((await new PracticeFlagJudge(db).JudgeAsync(new(fixture.CompetitionId, fixture.CompetitionChallengeId, joiner, fixture.Flag, fixture.Now.AddMinutes(6)), ct)).Judgement)
                .IsEqualTo(PracticeFlagJudgement.Correct);
            await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Closed_practice_rejects_late_team_creation_and_existing_teams_remain_formal(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var competition = await db.Competitions.SingleAsync(ct);
            competition.PracticeModeEnabled = false;
            await db.SaveChangesAsync(ct);
            var outbox = new RecordingOutbox();
            var store = new TeamRegistrationStore(db, outbox);
            var command = new CreateTeamCommand(fixture.CompetitionId, fixture.OwnerId, "Not open", fixture.Now, "default");
            await Assert.That((await new CreateTeam(store).ExecuteAsync(command, ct)).FailureCode).IsEqualTo(TeamRegistrationFailure.RegistrationClosed);
            await Assert.That((await store.TryCreateAsync(command, TeamRegistrationStatus.Approved, ct)).Failure).IsEqualTo(TeamRegistrationFailure.RegistrationClosed);
            await Assert.That(await db.Teams.CountAsync(ct)).IsEqualTo(1);
            await Assert.That((await db.Teams.SingleAsync(ct)).IsPracticeTeam).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task Practice_HTTP_returns_final_judgements_and_explicit_access_failures_without_formal_facts(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () => {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using (var setup = new NoCtfDbContext(options)) {
                (await setup.Challenges.SingleAsync(ct)).DefinitionJson = new GameModeChallengeConfigurationCatalog().GetDefaultDefinitionJson(GameMode.Ctf);
                await setup.SaveChangesAsync(ct);
            }
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddFastEndpoints(config => { config.DisableAutoDiscovery = true;
                config.Assemblies = [typeof(NoCTF.API.Endpoints.GameplayFacts.JudgePracticeFlagEndpoint).Assembly];
                config.Filter = type => type == typeof(NoCTF.API.Endpoints.GameplayFacts.JudgePracticeFlagEndpoint)
                    || type == typeof(NoCTF.API.Endpoints.GameplayFacts.JudgePracticeFlagValidator); });
            builder.Services.AddAuthentication("Bearer").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PracticeBearer>("Bearer", _ => { });
            builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<NoCTF.API.Security.IUserContext, PracticeActor>(); builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddDbContext<NoCtfDbContext>(db => db.UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
            builder.Services.AddScoped<IPracticeFlagJudge, PracticeFlagJudge>(); builder.Services.AddScoped<JudgePracticeFlag>();
            builder.Services.AddSingleton<NoCTF.Application.Runtime.Provisioning.IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
            builder.Services.AddRateLimiter(limit => limit.AddFixedWindowLimiter("submission", quota => { quota.PermitLimit = 100; quota.Window = TimeSpan.FromMinutes(1); }));
            await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseNoCtfEndpoints(); await app.StartAsync(ct);
            using var client = app.GetTestClient();
            var route = $"/api/v1/competitions/{fixture.CompetitionId}/challenges/{fixture.CompetitionChallengeId}/practice-flag";
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fixture.UserId.ToString());
            foreach (var value in new[] { fixture.Flag, "wrong", fixture.Flag }) {
                using var response = await client.PostAsJsonAsync(route, new { flag = value }, ct);
                await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
                await Assert.That((await response.Content.ReadFromJsonAsync<NoCTF.API.Endpoints.GameplayFacts.PracticeFlagJudgementResponse>(ct))!.Result)
                    .IsEqualTo(value == fixture.Flag ? NoCTF.API.Endpoints.GameplayFacts.PracticeFlagJudgementProtocol.Correct : NoCTF.API.Endpoints.GameplayFacts.PracticeFlagJudgementProtocol.Wrong);
            }
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Guid.NewGuid().ToString());
            using (var denied = await client.PostAsJsonAsync(route, new { flag = fixture.Flag }, ct)) await Assert.That(denied.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Authorization = null;
            using (var anonymous = await client.PostAsJsonAsync(route, new { flag = fixture.Flag }, ct)) await Assert.That(anonymous.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
            await using var verify = new NoCtfDbContext(options);
            await Assert.That(await verify.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await verify.CompetitionEvents.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await verify.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
        });
    }

    private sealed class PracticeActor(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : NoCTF.API.Security.IUserContext
    {
        public Guid UserId => Guid.Parse(accessor.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        public bool IsAdministrator => false;
    }
    private sealed class PracticeBearer(Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers.Authorization.ToString().Replace("Bearer ", ""), out var id)) return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
            return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }
    [Test]
    [Timeout(300_000)]
    public async Task Static_practice_flags_do_not_require_a_runtime_or_accept_unassigned_attachment_flags(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_static_practice").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var template = await db.Challenges.SingleAsync(ct);
            template.DefinitionJson = new GameModeChallengeConfigurationCatalog().GetDefaultDefinitionJson(GameMode.Ctf);
            var candidate = new ChallengeFlag
            {
                Id = Guid.CreateVersion7(), ChallengeId = fixture.ChallengeId,
                Flag = "flag{unassigned-attachment}",
                FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{unassigned-attachment}")),
                SpecificationKind = SpecificationKind.Attachment, SpecificationId = Guid.CreateVersion7(), CreatedAt = fixture.Now
            };
            db.ChallengeFlags.Add(candidate);
            await db.SaveChangesAsync(ct);
            var judge = new JudgePracticeFlag(new PracticeFlagJudge(db));
            var command = new JudgePracticeFlagCommand(fixture.CompetitionId, fixture.CompetitionChallengeId,
                fixture.UserId, fixture.Flag, fixture.Now);
            await Assert.That((await judge.ExecuteAsync(command, ct)).Judgement).IsEqualTo(PracticeFlagJudgement.Correct);
            await Assert.That((await judge.ExecuteAsync(command with { Flag = "wrong" }, ct)).Judgement).IsEqualTo(PracticeFlagJudgement.Wrong);
            await Assert.That((await judge.ExecuteAsync(command with { Flag = candidate.Flag }, ct)).Judgement).IsEqualTo(PracticeFlagJudgement.Wrong);

            db.ChallengeFlags.Add(new ChallengeFlag
            {
                Id = Guid.CreateVersion7(), CompetitionChallengeId = fixture.CompetitionChallengeId, TeamId = fixture.TeamId,
                Flag = candidate.Flag, FlagSha256 = candidate.FlagSha256, SpecificationKind = SpecificationKind.Attachment,
                SpecificationId = candidate.SpecificationId, CreatedAt = fixture.Now
            });
            await db.SaveChangesAsync(ct);
            await Assert.That((await judge.ExecuteAsync(command with { Flag = candidate.Flag }, ct)).Judgement).IsEqualTo(PracticeFlagJudgement.Correct);
            await Assert.That(await db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(0);

            var competition = await db.Competitions.SingleAsync(ct);
            competition.PracticeModeEnabled = false;
            await db.SaveChangesAsync(ct);
            await Assert.That((await judge.ExecuteAsync(command, ct)).FailureCode).IsEqualTo(PracticeFlagFailureCode.PracticeUnavailable);
            competition.PracticeModeEnabled = true;
            var team = await db.Teams.SingleAsync(ct);
            team.RegistrationStatus = TeamRegistrationStatus.Pending;
            await db.SaveChangesAsync(ct);
            await Assert.That((await judge.ExecuteAsync(command, ct)).FailureCode).IsEqualTo(PracticeFlagFailureCode.TeamNotEligible);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Finished_ctf_practice_runtime_can_judge_flags_without_scoring(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_practice")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, ct);

            await using (var history = new NoCtfDbContext(options))
            {
                history.RuntimeInstances.Add(Runtime(
                    fixture,
                    RuntimeState.Stopped,
                    RuntimePurpose.Player));
                await history.SaveChangesAsync(ct);
            }

            var outbox = new RecordingOutbox();
            await using (var db = new NoCtfDbContext(options))
            {
                var store = new RuntimeInstanceStore(
                    db,
                    new ChallengeRuntimeTemplateCatalog(),
                    new FixedRuntimePlacementPolicy(runnerPool: "practice-tests"),
                    new PostgresPerTeamRuntimeFlagStore(db),
                    outbox);
                var started = await store.MutatePlayerRuntimeAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    RuntimeAction.Start,
                    null,
                    fixture.Now), ct);

                await Assert.That(started.Failure).IsNull();
                await Assert.That(started.Runtime).IsNotNull();
                await Assert.That(outbox.Published.OfType<DispatchRuntime>()).HasSingleItem();
                var runtime = await db.RuntimeInstances.SingleAsync(
                    item => item.Purpose == RuntimePurpose.Practice,
                    ct);
                await Assert.That(runtime.Purpose).IsEqualTo(RuntimePurpose.Practice);
                runtime.State = RuntimeState.Running;
                runtime.RunningAt = fixture.Now;
                runtime.ExpiresAt = fixture.Now.AddMinutes(30);
                await db.SaveChangesAsync(ct);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var runtimeFlag = await db.ChallengeFlags.SingleAsync(
                    item => item.TeamId == fixture.TeamId
                        && item.SpecificationKind == SpecificationKind.RuntimeDefinition
                        && item.SpecificationId == fixture.CompetitionChallengeId,
                    ct);
                var judge = new JudgePracticeFlag(new PracticeFlagJudge(db));
                var correct = await judge.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    runtimeFlag.Flag,
                    fixture.Now.AddMinutes(1)), ct);
                var wrong = await judge.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    fixture.Flag,
                    fixture.Now.AddMinutes(1)), ct);

                await Assert.That(correct.Judgement)
                    .IsEqualTo(PracticeFlagJudgement.Correct);
                await Assert.That(wrong.Judgement)
                    .IsEqualTo(PracticeFlagJudgement.Wrong);
                await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
                await Assert.That(await db.CompetitionEvents.CountAsync(ct)).IsEqualTo(0);
                await Assert.That(await db.Notifications.CountAsync(ct)).IsEqualTo(0);
                runtimeFlag.Flag = @"flag\{practice-[a-z-]+\}";
                runtimeFlag.FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(runtimeFlag.Flag));
                runtimeFlag.MatchKind = ChallengeFlagMatchKind.RegularExpression;
                await db.SaveChangesAsync(ct);
                var regexRejected = await judge.ExecuteAsync(new(
                    fixture.CompetitionId,
                    fixture.CompetitionChallengeId,
                    fixture.UserId,
                    "flag{practice-dynamic}",
                    fixture.Now.AddMinutes(1)), ct);

                await Assert.That(regexRejected.Judgement)
                    .IsEqualTo(PracticeFlagJudgement.Wrong);
                await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var team = await db.Teams.SingleAsync(ct);
                team.IsBanned = true;
                await db.SaveChangesAsync(ct);
                var rejected = await new JudgePracticeFlag(new PracticeFlagJudge(db))
                    .ExecuteAsync(new(
                        fixture.CompetitionId,
                        fixture.CompetitionChallengeId,
                        fixture.UserId,
                        fixture.Flag,
                        fixture.Now.AddMinutes(2)), ct);
                await Assert.That(rejected.FailureCode)
                    .IsEqualTo(PracticeFlagFailureCode.TeamNotEligible);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Practice_mode_cannot_be_disabled_while_a_practice_runtime_is_active(
        CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_practice_disable")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            db.RuntimeInstances.Add(Runtime(fixture, RuntimeState.Running));
            await db.SaveChangesAsync(ct);
            var store = new CompetitionManagementStore(db);
            var current = await store.FindAsync(fixture.CompetitionId, true, ct);
            var updated = await store.UpdateAsync(new(
                fixture.CompetitionId,
                current!.Title,
                current.Description,
                current.StartTime,
                current.EndTime,
                current.TeamRegistrationAutoApprove,
                current.MaxTeamMembers,
                current.MaxConcurrentRuntimeInstancesPerTeam,
                fixture.OwnerId,
                fixture.Now.AddMinutes(1),
                current.AllowTeamRegistrationWhileRunning,
                current.MaxActiveQuestionsPerTeam,
                current.MaxParticipantMessagesBeforeHandlerReply,
                current.AllowChallengeOwnersToHandleQuestions,
                PracticeModeEnabled: false), ct);

            await Assert.That(updated).IsNull();
            await Assert.That(await db.Competitions
                .Select(item => item.PracticeModeEnabled)
                .SingleAsync(ct)).IsTrue();
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        var userId = Guid.CreateVersion7(now.AddTicks(1));
        var competitionId = Guid.CreateVersion7(now.AddTicks(2));
        var challengeId = Guid.CreateVersion7(now.AddTicks(3));
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(4));
        var teamId = Guid.CreateVersion7(now.AddTicks(5));
        const string flag = "flag{practice-is-unscored}";
        db.Users.AddRange(User(ownerId, "practice-owner", now), User(userId, "practice-player", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            Title = "Finished practice competition",
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Finished,
            PracticeModeEnabled = true,
            MaxConcurrentRuntimeInstancesPerTeam = 1,
            ConfigurationJson = """{"schemaVersion":2,"defaultScoreCurve":{"initialPoints":500,"minimumPoints":100,"decayTeamCount":10,"decayMode":2},"bloodRewards":[]}""",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-2),
            EndAt = now.AddHours(-1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Visibility = ChallengeVisibility.Private,
            Title = "Practice container",
            Direction = "Web",
            DefinitionJson = JsonSerializer.Serialize(
                new CtfChallengeConfiguration(
                    CtfChallengeConfiguration.CurrentSchemaVersion,
                    null,
                    null,
                    Runtime: new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
                        new ContainerRuntimeDefinition(
                            "registry.example/practice:v1",
                            FlagEnvironmentVariableName: "FLAG"),
                        new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                        FlagSource: RuntimeFlagSource.PerTeam)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            RulesJson = """{"schemaVersion":2}""",
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Practice Team",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = "0123456789abcdefghijklmnopqrstuv",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = Guid.CreateVersion7(now.AddTicks(6)),
            CompetitionChallengeId = competitionChallengeId,
            Flag = flag,
            FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        return new(now, ownerId, userId, competitionId, challengeId, competitionChallengeId, teamId, flag);
    }

    private static RuntimeInstance Runtime(
        Fixture fixture,
        RuntimeState state,
        RuntimePurpose purpose = RuntimePurpose.Practice) => new()
    {
        Id = Guid.CreateVersion7(fixture.Now.AddTicks(10)),
        CompetitionId = fixture.CompetitionId,
        CompetitionChallengeId = fixture.CompetitionChallengeId,
        TeamId = fixture.TeamId,
        Purpose = purpose,
        RuntimeKind = RuntimeKind.Container,
        RuntimeProvider = RuntimeProvider.Docker,
        State = state,
        CreatedAt = fixture.Now,
        RunningAt = state == RuntimeState.Running ? fixture.Now : null,
        ExpiresAt = fixture.Now.AddMinutes(30)
    };

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid UserId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        string Flag);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}

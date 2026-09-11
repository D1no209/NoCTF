using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Administration.Platform;
using NoCTF.API.Endpoints.Administration.Teams;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Authentication.Privacy;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class AccountPrivacyHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Incremental_migration_private_endpoints_scope_retention_and_authentication_events(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20260904120421_ChallengeTemplateTestRuntimes", ct);
            var existing = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (id,user_name,normalized_user_name,email,password_hash,kind,role,account_status,token_version,created_at,updated_at) VALUES ({existing},'existing','EXISTING','existing@example.test','unused',0,0,0,0,now(),now())", ct);
            await db.Database.MigrateAsync(ct);
            await Assert.That((await db.Users.SingleAsync(x => x.Id == existing, ct)).SchoolFullName).IsNull();
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            var owner = Guid.NewGuid(); var manager = Guid.NewGuid(); var judge = Guid.NewGuid(); var observer = Guid.NewGuid(); var outsider = Guid.NewGuid();
            foreach (var id in new[] { owner, manager, judge, observer, outsider })
                db.Users.Add(new User { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N"), Email = $"{id}@example.test",
                    PasswordHash = "unused", Kind = UserKind.Human, Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active,
                    CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
            var competition = await db.Competitions.SingleAsync(x => x.Id == fixture.Id, ct);
            competition.OwnerId = owner; competition.ManagerIds = [manager]; competition.JudgeIds = [judge]; competition.ObserverIds = [observer];
            var team = await db.Teams.SingleAsync(x => x.CompetitionId == fixture.Id, ct);
            team.MemberIds = [fixture.OwnerId, existing];
            var fact = await db.GameplayFacts.FirstAsync(x => x.CompetitionId == fixture.Id, ct);
            fact.ActorUserId = existing; fact.SourceIpAddress = "192.0.2.9";
            await db.SaveChangesAsync(ct); db.ChangeTracker.Clear();
            var source = new Source();
            var store = new AccountPrivacyStore(db, Options.Create(new AccountPrivacyOptions()), TimeProvider.System, source);
            await store.RecordLoginAsync(existing, fixture.Now, ct);
            await store.RecordLoginAsync(null, fixture.Now, ct);
            var failure = await db.Notifications.SingleAsync(x => x.Kind == NotificationKind.AuthenticationSecurityActivity && x.SourceId == null, ct);
            await Assert.That(failure.SourceType).IsEqualTo(NotificationSourceType.System);
            await Assert.That(JsonSerializer.Deserialize<AccountActivity>(failure.ContentJson)!.Kind).IsEqualTo(AccountActivityKind.LoginFailed);
            var registrationId = Guid.NewGuid();
            var auth = new AuthenticationStore(db, new PasswordHasher<User>(), source: source);
            await auth.RegisterAsync(registrationId, "new-user", "new@example.test", "long-password", fixture.Now, ct);
            await Assert.That(await db.Notifications.CountAsync(x => x.Kind == NotificationKind.AuthenticationSecurityActivity && x.SourceId == registrationId, ct)).IsEqualTo(1);

            await using var app = await AppAsync(postgres.GetConnectionString(), fixture.OwnerId, ct);
            using var client = app.GetTestClient();
            Authenticate(existing);
            using (var initial = await client.GetAsync("/api/v1/auth/me/profile", ct))
            {
                await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That((await initial.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(ct))!
                    .SchoolIdentity.StudentNumber).IsNull();
                await Assert.That(initial.Headers.CacheControl!.NoStore).IsTrue();
            }
            await SaveAsync("Test Name", "001Ab");
            using (var read = await client.GetAsync("/api/v1/auth/me/profile", ct))
                await Assert.That((await read.Content.ReadFromJsonAsync<CurrentUserProfileResponse>(ct))!
                    .SchoolIdentity.StudentNumber).IsEqualTo("001Ab");
            using (var invalid = await client.PatchAsJsonAsync("/api/v1/auth/me/profile",
                       new { schoolIdentity = new { fullName = new string('a', 101), studentNumber = (string?)null } }, ct))
                await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var path = $"/api/v1/admin/competitions/{fixture.Id}/teams/{team.Id}/members/{existing}/private-profile";
            foreach (var (actor, allowed) in new[] { (fixture.OwnerId, true), (owner, true), (manager, true), (judge, true), (observer, false), (outsider, false), (existing, false) })
            {
                Authenticate(actor);
                using var response = await client.GetAsync(path, ct);
                await Assert.That(response.IsSuccessStatusCode).IsEqualTo(allowed);
                if (!allowed) continue;
                var detail = (await response.Content.ReadFromJsonAsync<PrivateAccountResponse>(ct))!;
                await Assert.That(detail.Identity.StudentNumber).IsEqualTo("001Ab");
                await Assert.That(detail.Activities.All(x => x.CompetitionId == fixture.Id && x.Kind == "PatchUploaded")).IsTrue();
                await Assert.That(detail.Activities.Single(x => x.Id == fact.Id).IpAddress).IsEqualTo("192.0.2.9");
            }
            Authenticate(fixture.OwnerId);
            await db.Teams.Where(x => x.Id == team.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RegistrationStatus, NoCTF.Domain.Teams.TeamRegistrationStatus.Pending), ct);
            using (var pending = await client.GetAsync(path, ct))
                await Assert.That(pending.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await db.Teams.Where(x => x.Id == team.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RegistrationStatus, NoCTF.Domain.Teams.TeamRegistrationStatus.Rejected), ct);
            using (var rejected = await client.GetAsync(path, ct))
                await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await db.Teams.Where(x => x.Id == team.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RegistrationStatus, NoCTF.Domain.Teams.TeamRegistrationStatus.Approved), ct);
            using (var cross = await client.GetAsync(path.Replace(fixture.Id.ToString(), fixture.OtherId.ToString()), ct))
                await Assert.That(cross.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using (var detail = await client.GetAsync($"/api/v1/admin/platform/users/{existing}/activity", ct))
                await Assert.That((await detail.Content.ReadFromJsonAsync<PrivateAccountResponse>(ct))!.Activities.Any(x => x.Kind == "LoggedIn")).IsTrue();
            Authenticate(judge);
            using (var forbidden = await client.GetAsync($"/api/v1/admin/platform/users/{existing}/activity", ct))
                await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            Authenticate(existing); await SaveAsync(null, "");
            await Assert.That((await store.GetOwnAsync(existing, ct))!.StudentNumber).IsNull();
            // Student numbers have no uniqueness constraint.
            await store.SaveOwnAsync(existing, new("Test Name", "001Ab"), ct);
            await store.SaveOwnAsync(registrationId, new("Other", "001Ab"), ct);
            var profile = await auth.GetProfileAsync(existing, ct);
            await Assert.That(JsonSerializer.Serialize(profile)).Contains("001Ab");
            await Assert.That(JsonSerializer.Serialize(profile)).Contains("Test Name");
            var notifications = await new NotificationReader(db).ListAsync(fixture.OwnerId, null, null, 100, ct);
            await Assert.That(notifications.Any(x => x.Kind == NotificationKind.AuthenticationSecurityActivity)).IsFalse();
            var old = fixture.Now.AddDays(-31);
            await store.RecordLoginAsync(existing, old, ct);
            await db.GameplayFacts.Where(x => x.Id == fact.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.OccurredAt, old), ct);
            await store.RemoveExpiredAddressesAsync(ct);
            await store.RemoveExpiredAddressesAsync(ct);
            await Assert.That(await db.GameplayFacts.AsNoTracking().Where(x => x.Id == fact.Id).Select(x => x.SourceIpAddress).SingleAsync(ct)).IsNull();
            await Assert.That(await db.Notifications.AnyAsync(x => x.Kind == NotificationKind.AuthenticationSecurityActivity && x.SentAt < fixture.Now.AddDays(-30), ct)).IsFalse();
            await Assert.That(await db.Notifications.AnyAsync(x => x.Id == fixture.RootId, ct)).IsTrue();
            void Authenticate(Guid id) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", id.ToString());
            async Task SaveAsync(string? name, string? number)
            {
                using var response = await client.PatchAsJsonAsync("/api/v1/auth/me/profile",
                    new { schoolIdentity = new { fullName = name, studentNumber = number } }, ct);
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }
        });
    }
    private static async Task<WebApplication> AppAsync(string connection, Guid admin, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        Type[] endpoints = [typeof(GetMyProfileEndpoint), typeof(PatchMyProfileEndpoint), typeof(GetPrivateTeamMemberEndpoint), typeof(GetPrivatePlatformUserEndpoint)];
        builder.Services.AddFastEndpoints(o => { o.DisableAutoDiscovery = true; o.Assemblies = [typeof(GetMyProfileEndpoint).Assembly]; o.Filter = endpoints.Contains; });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, BearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(new Administrator(admin));
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddOptions<AccountPrivacyOptions>();
        builder.Services.AddScoped<IUserContext, Actor>(); builder.Services.AddScoped<IAccountPrivacyStore, AccountPrivacyStore>();
        builder.Services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>(); builder.Services.AddScoped<AccountPrivacy>();
        builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddScoped<AuthenticationStore>();
        builder.Services.AddScoped<IUserAuthenticationStore>(provider => provider.GetRequiredService<AuthenticationStore>());
        builder.Services.AddScoped<ICurrentUserProfilePatchStore>(provider => provider.GetRequiredService<AuthenticationStore>());
        builder.Services.AddScoped<GetCurrentUser>();
        builder.Services.AddScoped<PatchCurrentUserProfile>();
        builder.Services.AddDbContext<NoCtfDbContext>(o => o.UseNpgsql(connection).UseSnakeCaseNamingConvention());
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync(ct); return app;
    }
    private sealed record Administrator(Guid Id);
    private sealed class Source : IRequestSourceAddress { public string Address => "2001:db8::7"; }
    private sealed class Actor(IHttpContextAccessor context) : IUserContext
    {
        public Guid UserId => Guid.Parse(context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        public bool IsAdministrator => context.HttpContext!.User.IsInRole("Administrator");
        public string? Role => context.HttpContext!.User.FindFirstValue(ClaimTypes.Role);
    }
    private sealed class BearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Administrator admin)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers.Authorization.ToString().Replace("Bearer ", ""), out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, id == admin.Id ? "Administrator" : "Organizer")], Scheme.Name)), Scheme.Name)));
        }
    }
}

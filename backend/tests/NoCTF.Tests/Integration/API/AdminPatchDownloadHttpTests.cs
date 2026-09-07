using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.GameplayFacts;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.GameplayFacts.Administration;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class AdminPatchDownloadHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Staff_downloads_are_scoped_streamed_and_audited_including_administrators(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var root = Path.Combine(Path.GetTempPath(), $"noctf-patch-download-{Guid.NewGuid():N}");
            try
            {
                using var disk = StorageFactory.Disk(root);
                var faults = new Faults();
                var storage = new ObservedStore(disk, faults);
                var fixture = await SeedAsync(options, disk, ct);
                await using var app = await ApplicationAsync(postgres.GetConnectionString(), storage, faults, ct);
                using var client = app.GetTestClient();
                var successful = 0;

                foreach (var (actor, allowed) in fixture.Roles.Select(pair => (pair.Value,
                    pair.Key is Role.Administrator or Role.Owner or Role.Manager or Role.Judge)))
                {
                    Authenticate(actor);
                    using var detailResponse = await client.GetAsync(DetailUri(fixture.Scope.Id, fixture.FactId), ct);
                    if (detailResponse.IsSuccessStatusCode)
                    {
                        var detail = await detailResponse.Content.ReadFromJsonAsync<AdminGameplayFactStatusResponse>(ct);
                        await Assert.That(detail!.CanDownloadPatch).IsEqualTo(allowed);
                        await Assert.That(detail.Patch is not null).IsEqualTo(allowed);
                        if (allowed)
                        {
                            await Assert.That(detail.Patch!.FileName).IsEqualTo("原始 Patch.tar.gz");
                            await Assert.That(detail.Patch.ByteLength).IsEqualTo(fixture.Bytes.Length);
                            await Assert.That(detail.Patch.Sha256).IsEqualTo(Convert.ToHexString(SHA256.HashData(fixture.Bytes)));
                            await Assert.That(detail.Patch.UploadedAt.ToUnixTimeMilliseconds()).IsEqualTo(fixture.UploadedAt.ToUnixTimeMilliseconds());
                        }
                        var json = JsonSerializer.Serialize(detail);
                        await Assert.That(json).DoesNotContain(fixture.ObjectKey);
                        await Assert.That(json).DoesNotContain(root);
                    }
                    else await Assert.That(allowed).IsFalse();
                    if (allowed) await DownloadAsync(actor);
                    else await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.Forbidden, "Forbidden");
                }
                // A stale management page must not retain a revoked download permission.
                await using (var db = new NoCtfDbContext(options))
                {
                    var competition = await db.Competitions.SingleAsync(x => x.Id == fixture.Scope.Id, ct);
                    competition.ManagerIds = [];
                    await db.SaveChangesAsync(ct);
                    Authenticate(fixture.Roles[Role.Manager]);
                    await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.Forbidden, "Forbidden");
                    competition.ManagerIds = [fixture.Roles[Role.Manager]];
                    await db.SaveChangesAsync(ct);
                }
                client.DefaultRequestHeaders.Authorization = null;
                using (var anonymous = await client.GetAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), ct))
                    await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
                Authenticate(fixture.Roles[Role.Administrator]);
                await FailureAsync(DownloadUri(fixture.Scope.OtherId, fixture.FactId), HttpStatusCode.NotFound, "SubmissionNotFound");
                await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FlagId), HttpStatusCode.BadRequest, "NotFixSubmission");

                await using (var db = new NoCtfDbContext(options))
                {
                    var fact = await db.GameplayFacts.SingleAsync(x => x.Id == fixture.FactId, ct);
                    var reference = fact.ReferenceId;
                    fact.ReferenceId = Guid.NewGuid();
                    await db.SaveChangesAsync(ct);
                    await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.NotFound, "PatchNotFound");
                    fact.ReferenceId = reference;
                    await db.SaveChangesAsync(ct);
                    var patch = await db.PatchUploads.SingleAsync(x => x.Id == reference, ct);
                    var originalTeam = patch.TeamId;
                    var originalChallenge = patch.CompetitionChallengeId;
                    foreach (var tamper in Enum.GetValues<Tamper>())
                    {
                        switch (tamper)
                        {
                            case Tamper.Competition: patch.CompetitionId = fixture.Scope.OtherId; break;
                            case Tamper.Challenge: patch.CompetitionChallengeId = fixture.OtherChallengeId; break;
                            case Tamper.Team: patch.TeamId = fixture.OtherTeamId; break;
                            case Tamper.Actor: patch.UploadedByUserId = fixture.Roles[Role.Owner]; break;
                            case Tamper.BothChallenges: fact.CompetitionChallengeId = patch.CompetitionChallengeId = fixture.OtherChallengeId; break;
                            case Tamper.BothTeams: fact.TeamId = patch.TeamId = fixture.OtherTeamId; break;
                        }
                        await db.SaveChangesAsync(ct);
                        await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.Conflict, "InvalidAssociation");
                        patch.CompetitionId = fixture.Scope.Id;
                        fact.CompetitionChallengeId = patch.CompetitionChallengeId = originalChallenge;
                        fact.TeamId = patch.TeamId = originalTeam;
                        patch.UploadedByUserId = fixture.Roles[Role.Participant];
                        await db.SaveChangesAsync(ct);
                    }
                    foreach (var (state, result) in new (GameplayFactState, GameplayFactResult?)[]
                    {
                        (GameplayFactState.Pending, null), (GameplayFactState.Processing, null),
                        (GameplayFactState.Completed, GameplayFactResult.Correct),
                        (GameplayFactState.Completed, GameplayFactResult.Wrong),
                        (GameplayFactState.Completed, GameplayFactResult.Rejected),
                        (GameplayFactState.PlatformFailed, null)
                    })
                    {
                        fact.State = state;
                        fact.Result = result;
                        await db.SaveChangesAsync(ct);
                        await DownloadAsync(fixture.Roles[Role.Administrator]);
                    }
                }
                await disk.DeleteObject(fixture.ObjectKey, ct);
                await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.NotFound, "FileNotFound");
                await using (var bytes = new MemoryStream(fixture.Bytes))
                    await disk.SetObject(fixture.ObjectKey, bytes, "text/html", cancellationToken: ct);
                faults.Storage = true;
                await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.ServiceUnavailable, "StorageUnavailable");
                faults.Storage = false;
                faults.Audit = true;
                await FailureAsync(DownloadUri(fixture.Scope.Id, fixture.FactId), HttpStatusCode.ServiceUnavailable, "AuditUnavailable");
                await Assert.That(storage.LastOpened!.CanRead).IsFalse();
                faults.Audit = false;

                await using var verify = new NoCtfDbContext(options);
                var events = await verify.CompetitionEvents.Where(x => x.Kind == CompetitionEventKind.GameplayFactPatchDownloaded).ToArrayAsync(ct);
                await Assert.That(events.Length).IsEqualTo(successful);
                await Assert.That(events.Select(x => x.ActorUserId).Distinct()).IsEquivalentTo(fixture.Roles
                    .Where(x => x.Key is Role.Administrator or Role.Owner or Role.Manager or Role.Judge).Select(x => (Guid?)x.Value));
                await Assert.That(events.All(x => x.Visibility == CompetitionEventVisibility.Staff && x.GameplayFactId == fixture.FactId
                    && x.TeamId == fixture.TeamId && x.RelatedId == fixture.FileId && x.OccurredAt >= fixture.Scope.Now)).IsTrue();
                await Assert.That(events.All(x => !x.PayloadJson.Contains(fixture.ObjectKey, StringComparison.Ordinal)
                    && !x.PayloadJson.Contains("sensitive-patch-content", StringComparison.Ordinal))).IsTrue();
                var audits = await new PlatformAuditLogStore(verify).QueryAsync(new(PlatformAuditKind.CompetitionEvent, null, null,
                    fixture.Scope.Id, null, null, null, 100), ct);
                await Assert.That(audits.Count).IsEqualTo(successful);
                await Assert.That(audits.All(x => x.FileId == fixture.FileId && x.TeamId == fixture.TeamId && x.GameplayFactId == fixture.FactId)).IsTrue();

                void Authenticate(Guid id) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", id.ToString());
                async Task DownloadAsync(Guid actor)
                {
                    Authenticate(actor);
                    using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUri(fixture.Scope.Id, fixture.FactId));
                    request.Headers.Range = new RangeHeaderValue(0, 1); // Ranges cannot bypass a fresh authorization/audit.
                    using var response = await client.SendAsync(request, ct);
                    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
                    await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/octet-stream");
                    await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
                    await Assert.That(response.Content.Headers.ContentDisposition.FileNameStar).IsEqualTo("原始 Patch.tar.gz");
                    await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
                    await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
                    await Assert.That((await response.Content.ReadAsByteArrayAsync(ct)).SequenceEqual(fixture.Bytes)).IsTrue();
                    successful++;
                }
                async Task FailureAsync(string uri, HttpStatusCode expectedStatus, string expectedCode)
                {
                    using var response = await client.GetAsync(uri, ct);
                    var text = await response.Content.ReadAsStringAsync(ct);
                    await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
                    using var json = JsonDocument.Parse(text);
                    await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(expectedCode);
                    await Assert.That(text).DoesNotContain(fixture.ObjectKey);
                    await Assert.That(text).DoesNotContain("sensitive-patch-content");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    private static string DetailUri(Guid competition, Guid fact) => $"/api/v1/admin/competitions/{competition}/gameplay-facts/{fact}";
    private static string DownloadUri(Guid competition, Guid fact) => DetailUri(competition, fact) + "/patch";
    private enum Role { Administrator, Owner, Manager, Judge, Observer, Participant, OtherCompetitionOwner }
    private enum Tamper { Competition, Challenge, Team, Actor, BothChallenges, BothTeams }

    private static async Task<Fixture> SeedAsync(DbContextOptions<NoCtfDbContext> options, IStore disk, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var scope = new CompetitionForceDeleteFixture();
        await scope.SeedAsync(db, ct);
        var roles = Enum.GetValues<Role>().ToDictionary(x => x, x => x == Role.Administrator ? scope.OwnerId : Guid.NewGuid());
        foreach (var (role, id) in roles.Where(x => x.Key != Role.Administrator))
            db.Users.Add(new User { Id = id, UserName = id.ToString("N"), NormalizedUserName = id.ToString("N"),
                Email = $"{id:N}@example.test", PasswordHash = "unused", Kind = UserKind.Human,
                Role = role == Role.Participant ? UserRole.User : UserRole.Organizer, AccountStatus = UserAccountStatus.Active,
                CreatedAt = scope.Now, UpdatedAt = scope.Now });
        var competition = await db.Competitions.SingleAsync(x => x.Id == scope.Id, ct);
        competition.OwnerId = roles[Role.Owner]; competition.ManagerIds = [roles[Role.Manager]];
        competition.JudgeIds = [roles[Role.Judge]]; competition.ObserverIds = [roles[Role.Observer]];
        (await db.Competitions.SingleAsync(x => x.Id == scope.OtherId, ct)).OwnerId = roles[Role.OtherCompetitionOwner];
        var fact = await db.GameplayFacts.FirstAsync(x => x.CompetitionId == scope.Id, ct);
        var patch = await db.PatchUploads.SingleAsync(x => x.Id == fact.ReferenceId, ct);
        fact.ActorUserId = patch.UploadedByUserId = roles[Role.Participant];
        fact.State = GameplayFactState.PlatformFailed; fact.Result = null;
        var team = await db.Teams.SingleAsync(x => x.Id == fact.TeamId, ct);
        team.MemberIds = [scope.OwnerId, roles[Role.Participant]];
        var otherTeam = Guid.NewGuid();
        db.Teams.Add(new Team { Id = otherTeam, CompetitionId = scope.OtherId, Name = "Other team",
            CaptainId = roles[Role.OtherCompetitionOwner], MemberIds = [roles[Role.OtherCompetitionOwner]],
            InvitationToken = Guid.NewGuid().ToString("N"), RegisteredAt = scope.Now, RegistrationStatus = TeamRegistrationStatus.Approved });
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("sensitive-patch-content\0\n<script>not executable on download</script>\n");
        var key = $"private-original-patches/{fileId:N}";
        db.Files.Add(new StoredFile { Id = fileId, ObjectKey = key, FileName = "原始 Patch.tar.gz", ContentType = "text/html",
            ByteLength = content.Length, Sha256 = SHA256.HashData(content), CreatedAt = scope.Now });
        patch.FileId = fileId;
        var flagId = Guid.NewGuid();
        db.GameplayFacts.Add(new GameplayFact { Id = flagId, CompetitionId = scope.Id, CompetitionChallengeId = fact.CompetitionChallengeId,
            TeamId = fact.TeamId, ActorUserId = roles[Role.Participant], Kind = GameplayFactKind.FlagAttempt, State = GameplayFactState.Completed,
            Result = GameplayFactResult.Wrong, Value = "flag{test}", ValueSha256 = new byte[32], OccurredAt = scope.Now, UpdatedAt = scope.Now });
        await db.SaveChangesAsync(ct);
        await using var bytes = new MemoryStream(content);
        await disk.SetObject(key, bytes, "text/html", cancellationToken: ct);
        return new(scope, roles, fact.Id, flagId, fileId, team.Id,
            await db.CompetitionChallenges.Where(x => x.CompetitionId == scope.OtherId).Select(x => x.Id).SingleAsync(ct),
            otherTeam, key, content, patch.UploadedAt);
    }

    private static async Task<WebApplication> ApplicationAsync(string connection, IStore storage, Faults faults, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(DownloadAdminGameplayFactPatchEndpoint).Assembly];
            options.Filter = type => type == typeof(DownloadAdminGameplayFactPatchEndpoint) || type == typeof(GetAdminGameplayFactStatusEndpoint); });
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, BearerHandler>("Bearer", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, ActorContext>(); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(connection).UseSnakeCaseNamingConvention().AddInterceptors(new AuditFailure(faults)));
        builder.Services.AddSingleton(storage); builder.Services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
        builder.Services.AddScoped<IAdminPatchDownloadStore, AdminPatchDownloadStore>(); builder.Services.AddScoped<AccessAdminPatch>();
        builder.Services.AddScoped<IAdminGameplayFactStatusReader, AdminGameplayFactStatusReader>();
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync(ct); return app;
    }
    private sealed record Fixture(CompetitionForceDeleteFixture Scope, Dictionary<Role, Guid> Roles, Guid FactId, Guid FlagId,
        Guid FileId, Guid TeamId, Guid OtherChallengeId, Guid OtherTeamId, string ObjectKey, byte[] Bytes, DateTimeOffset UploadedAt);
    private sealed class Faults { public bool Audit; public bool Storage; }
    private sealed class AuditFailure(Faults faults) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (faults.Audit && data.Context!.ChangeTracker.Entries<CompetitionEvent>().Any(x => x.Entity.Kind == CompetitionEventKind.GameplayFactPatchDownloaded))
                throw new DbUpdateException("Injected audit persistence failure.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ObservedStore(IStore inner, Faults faults) : StoreBase
    {
        public Stream? LastOpened { get; private set; }
        public override Task<bool> ObjectExists(string key, CancellationToken ct = default) => faults.Storage ? throw new IOException("Injected storage failure.") : inner.ObjectExists(key, ct);
        public override async Task<Stream> OpenRead(string key, CancellationToken ct = default) => LastOpened = await inner.OpenRead(key, ct);
    }
    private sealed class ActorContext(IHttpContextAccessor context) : IUserContext
    {
        public Guid UserId => Guid.Parse(context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        public bool IsAdministrator => false; // Authorization must consult the database, not a UI/client role.
    }
    private sealed class BearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !Guid.TryParse(header[7..], out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }
}

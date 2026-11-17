using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NoCTF.API.Endpoints.Administration.Competitions.Credentials;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Credentials;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Competitions.Credentials;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;
using Wolverine;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class CompetitionCredentialsHttpTests
{
    private const string Header = "username,email,teamName,captain,trackKey,fullName,studentNumber,school\n";
    private const string Participants = Header + "alice,,Blue,true,,张三,001234,成都理工大学\nbob,,Blue,false,,,,\ncharlie,charlie@example.test,Red,true,,,,\n";

    [Test, Timeout(300_000)]
    public async Task Imported_accounts_authenticate_and_preserve_team_captain_metadata_and_password_rotation(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        using var client = fixture.Client(fixture.OwnerId);
        using var imported = await fixture.ImportAsync(client, Participants, ct);
        await Assert.That(imported.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await imported.Content.ReadFromJsonAsync<ImportCompetitionCredentialsResponse>(ct))!;
        await Assert.That(result.CreatedUsers).IsEqualTo(3);
        await Assert.That(result.CreatedTeams).IsEqualTo(2);
        var batchPath = fixture.Path + "/" + result.BatchId;
        await Assert.That(imported.Headers.Location!.OriginalString).IsEqualTo(batchPath);
        using var batchResponse = await client.GetAsync(batchPath, ct);
        await Assert.That(batchResponse.Headers.CacheControl!.NoStore).IsTrue();
        var batch = (await batchResponse.Content.ReadFromJsonAsync<GetCompetitionCredentialBatchResponse>(ct))!;
        var alice = batch.Rows.Single(row => row.UserName == "alice");
        await Assert.That(alice.FullName).IsEqualTo("张三");
        await Assert.That(alice.StudentNumber).IsEqualTo("001234");
        await Assert.That(alice.School).IsEqualTo("成都理工大学");
        await Assert.That(batch.Rows.Select(row => row.Password).Distinct().Count()).IsEqualTo(3);
        await Assert.That(batch.Rows.All(row => row.Password.Length == 16 && char.IsAsciiLetterOrDigit(row.Password[0]))).IsTrue();
        await using (var db = fixture.Database())
        {
            var auth = new AuthenticationStore(db, fixture.Hasher);
            foreach (var row in batch.Rows)
            {
                await Assert.That((await auth.FindByLoginAsync(row.UserName.ToUpperInvariant(), ct))!.Id).IsEqualTo(row.UserId);
                await Assert.That(await auth.VerifyPasswordAsync(row.UserId, row.Password, ct)).IsTrue();
            }
            var team = await db.Teams.SingleAsync(team => team.Name == "Blue", ct);
            await Assert.That(team.CaptainId).IsEqualTo(alice.UserId);
            await Assert.That(team.Members.Count).IsEqualTo(2);
            await Assert.That(team.InvitationToken.Length).IsEqualTo(32);
            await Assert.That(team.InvitationToken.All(char.IsAsciiLetterOrDigit)).IsTrue();
            await Assert.That(team.RegistrationStatus).IsEqualTo(TeamRegistrationStatus.Approved);
            await Assert.That(await db.CompetitionEvents.CountAsync(value => value.Kind == CompetitionEventKind.TeamRegistered, ct)).IsEqualTo(2);
            var protectedPassword = await db.CompetitionCredentials.Where(value => value.UserId == alice.UserId).Select(value => value.PasswordCiphertext).SingleAsync(ct);
            await Assert.That(Encoding.UTF8.GetString(protectedPassword).Contains(alice.Password, StringComparison.Ordinal)).IsFalse();
        }
        var repeated = (await client.GetFromJsonAsync<GetCompetitionCredentialBatchResponse>(batchPath, ct))!;
        await Assert.That(repeated.Rows.Single(row => row.UserId == alice.UserId).Password).IsEqualTo(alice.Password);
        using var rotated = await client.PostAsync($"/api/v1/admin/competitions/{fixture.CompetitionId}/credential-users/{alice.UserId}/rotate", null, ct);
        await Assert.That(rotated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(rotated.Headers.CacheControl!.NoStore).IsTrue();
        var replacement = (await rotated.Content.ReadFromJsonAsync<RotateCompetitionCredentialUserResponse>(ct))!;
        await using (var db = fixture.Database())
        {
            var auth = new AuthenticationStore(db, fixture.Hasher);
            await Assert.That(await auth.VerifyPasswordAsync(alice.UserId, alice.Password, ct)).IsFalse();
            await Assert.That(await auth.VerifyPasswordAsync(alice.UserId, replacement.Password, ct)).IsTrue();
            await Assert.That((await db.Users.SingleAsync(user => user.Id == alice.UserId, ct)).TokenVersion).IsEqualTo(1);
        }
        using var rotateBatch = await client.PostAsync(batchPath + "/rotate", null, ct);
        await Assert.That(rotateBatch.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var updated = (await client.GetFromJsonAsync<GetCompetitionCredentialBatchResponse>(batchPath, ct))!;
        await using (var db = fixture.Database())
        {
            var auth = new AuthenticationStore(db, fixture.Hasher);
            foreach (var row in updated.Rows)
                await Assert.That(await auth.VerifyPasswordAsync(row.UserId, row.Password, ct)).IsTrue();
            await Assert.That(await auth.VerifyPasswordAsync(alice.UserId, replacement.Password, ct)).IsFalse();
        }
    }

    [Test, Timeout(300_000)]
    public async Task All_existing_identity_conflicts_are_reported_and_the_entire_batch_is_rejected(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        using var client = fixture.Client(fixture.OwnerId);
        using var first = await fixture.ImportAsync(client, Participants, ct);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var rejected = await fixture.ImportAsync(client, Header + "ALICE,charlie@example.test,Other,true,,,,\nfresh,,blue,true,,,,\n", ct);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var result = (await rejected.Content.ReadFromJsonAsync<ImportCompetitionCredentialsResponse>(ct))!;
        await Assert.That(result.Issues.Any(issue => issue.RowNumber == 2 && issue.Code == CompetitionCredentialImportIssueCode.ExistingUserName)).IsTrue();
        await Assert.That(result.Issues.Any(issue => issue.RowNumber == 2 && issue.Code == CompetitionCredentialImportIssueCode.ExistingEmail)).IsTrue();
        await Assert.That(result.Issues.Any(issue => issue.RowNumber == 3 && issue.Code == CompetitionCredentialImportIssueCode.ExistingTeam)).IsTrue();
        await using var db = fixture.Database();
        await Assert.That(await db.Users.CountAsync(ct)).IsEqualTo(4);
        await Assert.That(await db.Teams.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await db.CompetitionCredentialBatches.CountAsync(ct)).IsEqualTo(1);
    }

    [Test, Timeout(300_000)]
    public async Task Duplicate_rows_captains_and_member_limits_have_line_numbers_and_no_partial_writes(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        await using (var db = fixture.Database())
        {
            (await db.Competitions.SingleAsync(ct)).MaxTeamMembers = 1;
            await db.SaveChangesAsync(ct);
        }
        using var client = fixture.Client(fixture.OwnerId);
        using var response = await fixture.ImportAsync(client, Header + "alice,same@example.test,Blue,true,,,,\nALICE,same@example.test,Blue,true,,,,\n", ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var result = (await response.Content.ReadFromJsonAsync<ImportCompetitionCredentialsResponse>(ct))!;
        foreach (var code in new[] { CompetitionCredentialImportIssueCode.DuplicateUserName, CompetitionCredentialImportIssueCode.DuplicateEmail,
                     CompetitionCredentialImportIssueCode.TeamCaptainConflict, CompetitionCredentialImportIssueCode.TeamMemberLimitExceeded })
            await Assert.That(result.Issues.Count(issue => issue.Code == code && issue.RowNumber >= 2)).IsEqualTo(2);
        await using var verify = fixture.Database();
        await Assert.That(await verify.Users.CountAsync(ct)).IsEqualTo(1);
        await Assert.That(await verify.Teams.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await verify.CompetitionCredentialBatches.CountAsync(ct)).IsEqualTo(0);
    }

    [Test, Timeout(300_000)]
    public async Task Observer_is_denied_and_judge_can_import_and_download_template(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        var observer = await fixture.AddCollaboratorAsync(CompetitionCollaboratorRole.Observer, ct);
        var judge = await fixture.AddCollaboratorAsync(CompetitionCollaboratorRole.Judge, ct);
        using var observerClient = fixture.Client(observer);
        using var denied = await fixture.ImportAsync(observerClient, Participants, ct);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var judgeClient = fixture.Client(judge);
        using var imported = await fixture.ImportAsync(judgeClient, Participants, ct);
        await Assert.That(imported.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await imported.Content.ReadFromJsonAsync<ImportCompetitionCredentialsResponse>(ct))!;
        using var hidden = await observerClient.GetAsync(fixture.Path + "/" + result.BatchId, ct);
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var template = await judgeClient.GetAsync(fixture.Path + "/import-template.csv", ct);
        await Assert.That(template.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await template.Content.ReadAsStringAsync(ct)).Contains("fullName,studentNumber,school");
    }

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Concurrent_imports_cannot_create_duplicate_teams_or_partial_batches(bool sameUser, CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        using var first = fixture.Client(fixture.OwnerId);
        using var second = fixture.Client(fixture.OwnerId);
        var responses = await Task.WhenAll(
            fixture.ImportAsync(first, Header + "alice,,Blue,true,,,,\n", ct),
            fixture.ImportAsync(second, Header + (sameUser ? "alice" : "bob") + ",,Blue,true,,,,\n", ct));
        try
        {
            await Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.Created)).IsEqualTo(1);
            await Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest)).IsEqualTo(1);
            await using var db = fixture.Database();
            await Assert.That(await db.Teams.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(await db.Users.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await db.CompetitionCredentialBatches.CountAsync(ct)).IsEqualTo(1);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Test, Timeout(300_000)]
    public async Task Downloads_include_optional_fields_and_do_not_execute_csv_formulas_or_html(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(ct);
        using var client = fixture.Client(fixture.OwnerId);
        using var imported = await fixture.ImportAsync(client, Header + "alice,,=1+1,true,,<script>alert(1)</script>,001234,学校\n", ct);
        await Assert.That(imported.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await imported.Content.ReadFromJsonAsync<ImportCompetitionCredentialsResponse>(ct))!;
        using var response = await client.GetAsync(fixture.Path + "/" + result.BatchId + "/download", ct);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync(ct));
        using var csvReader = new StreamReader(archive.GetEntry("credentials.csv")!.Open());
        var csv = await csvReader.ReadToEndAsync(ct);
        await Assert.That(csv).Contains("\"'=1+1\"");
        await Assert.That(csv).Contains("001234");
        using var htmlReader = new StreamReader(archive.GetEntry("credentials.html")!.Open());
        var html = await htmlReader.ReadToEndAsync(ct);
        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).DoesNotContain("<script>");
        await Assert.That(archive.GetEntry("credentials.pdf")!.Length).IsGreaterThan(1000);
        var evidence = System.IO.Path.Combine(AppContext.BaseDirectory, "TestResults", "credentials-export.pdf");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(evidence)!);
        await using var pdf = archive.GetEntry("credentials.pdf")!.Open();
        await using var output = File.Create(evidence);
        await pdf.CopyToAsync(output, ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        private readonly IContainer nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.14.7-alpine@sha256:4063edae0717ba5f7501bfde75f97fd9b57f5b93597b92c70b6a6fbbf6a74e06")
            .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
        private readonly string signingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        private IHost? messaging;
        private WebApplication? app;
        private DbContextOptions<NoCtfDbContext> options = null!;
        public Guid OwnerId { get; } = Guid.NewGuid();
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public PasswordHasher<User> Hasher { get; } = new();
        public string Path => $"/api/v1/admin/competitions/{CompetitionId}/credential-batches";
        public NoCtfDbContext Database() => new(options);

        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var fixture = new Fixture();
            try { await fixture.StartAsync(ct); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private async Task StartAsync(CancellationToken ct)
        {
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(), provider =>
                provider.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            await using (var db = Database())
            {
                await db.Database.MigrateAsync(ct);
                var now = TimeProvider.System.GetUtcNow();
                db.Users.Add(new() { Id = OwnerId, UserName = "organizer", NormalizedUserName = "ORGANIZER", Email = "organizer@example.test",
                    Kind = UserKind.Human, Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active, EmailVerifiedAt = now });
                db.Competitions.Add(new CtfCompetition { Id = CompetitionId, OwnerId = OwnerId, Title = "Credential integration",
                    Status = CompetitionStatus.Published, StartAt = now.AddHours(1), EndAt = now.AddHours(3), FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
                    Tracks = CompetitionTrackConfiguration.ToPersisted(CompetitionTrackConfiguration.DefaultFor(GameMode.Ctf), CompetitionId),
                    ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, CompetitionId) });
                await db.SaveChangesAsync(ct);
            }
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:Nats"] = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" }).Build();
            messaging = Host.CreateDefaultBuilder().UseWolverine(wolverine =>
            {
                wolverine.Discovery.DisableConventionalDiscovery();
                wolverine.ConfigureNoCtfPersistence(configuration, HostRoles.Only(HostRole.Api));
                wolverine.ConfigureNoCtfMessageRouting(configuration, HostRoles.Only(HostRole.Api));
            }).Build();
            await messaging.StartAsync(ct);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(options);
            builder.Services.AddScoped<NoCtfDbContext>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IUserContext, HttpUserContext>();
            builder.Services.AddSingleton<IPasswordHasher<User>>(Hasher);
            builder.Services.AddSingleton(new PlatformSecretProtector(Options.Create(new EmailVerificationProtectionOptions
                { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })));
            builder.Services.AddSingleton<IDatabaseConstraintClassifier, PostgreSqlConstraintClassifier>();
            builder.Services.AddScoped<IPostCommitMessagePublisher>(services => new WolverinePostCommitMessagePublisher(
                messaging.Services.GetRequiredService<IMessageBus>(), services.GetRequiredService<NoCtfDbContext>()));
            builder.Services.AddScoped<ICompetitionEventRecorder, CompetitionEventStore>();
            builder.Services.AddScoped<ICompetitionModerationAuthorizer, CompetitionModerationAuthorizer>();
            builder.Services.AddScoped<ICompetitionCredentialStore, CompetitionCredentialStore>();
            builder.Services.AddScoped<ImportCompetitionCredentials>();
            builder.Services.AddScoped<ReadCompetitionCredentialBatch>();
            builder.Services.AddScoped<ListCompetitionCredentialBatches>();
            builder.Services.AddScoped<RotateCompetitionCredentialBatch>();
            builder.Services.AddScoped<RotateCompetitionCredentialUser>();
            builder.Services.AddSingleton<ICompetitionCredentialImportReader, CompetitionCredentialImportReader>();
            builder.Services.AddSingleton<CompetitionCredentialExportService>();
            builder.Services.AddFastEndpoints(endpoint => { endpoint.DisableAutoDiscovery = true;
                endpoint.Assemblies = [typeof(ImportCompetitionCredentialsEndpoint).Assembly];
                endpoint.Filter = type => type.Namespace == typeof(ImportCompetitionCredentialsEndpoint).Namespace; });
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt => jwt.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateIssuer = true, ValidIssuer = "credential-test", ValidateAudience = true, ValidAudience = "credential-test", ValidateLifetime = true
            });
            builder.Services.AddAuthorization();
            app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization();
            app.UseFastEndpoints(endpoint => endpoint.Endpoints.RoutePrefix = "api/v1");
            await app.StartAsync(ct);
        }

        public HttpClient Client(Guid actor)
        {
            var client = app!.GetTestClient();
            var now = TimeProvider.System.GetUtcNow().UtcDateTime;
            var token = new JwtSecurityToken("credential-test", "credential-test", [new Claim("sub", actor.ToString())], now, now.AddMinutes(30),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            return client;
        }

        public async Task<HttpResponseMessage> ImportAsync(HttpClient client, string csv, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "File", "participants.csv");
            return await client.PostAsync(Path + "/import", form, ct);
        }

        public async Task<Guid> AddCollaboratorAsync(CompetitionCollaboratorRole role, CancellationToken ct)
        {
            await using var db = Database();
            var id = Guid.NewGuid();
            db.Users.Add(new() { Id = id, UserName = role.ToString(), NormalizedUserName = role.ToString().ToUpperInvariant(),
                Email = $"{role}@example.test", Kind = UserKind.Human, Role = UserRole.Organizer,
                AccountStatus = UserAccountStatus.Active, EmailVerifiedAt = TimeProvider.System.GetUtcNow() });
            (await db.Competitions.SingleAsync(ct)).Collaborators.Add(new() { CompetitionId = CompetitionId, UserId = id, Role = role });
            await db.SaveChangesAsync(ct);
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            if (app is not null) await app.DisposeAsync();
            if (messaging is not null) { await messaging.StopAsync(); messaging.Dispose(); }
            Npgsql.NpgsqlConnection.ClearAllPools();
            await nats.DisposeAsync(); await postgres.DisposeAsync();
        }
    }
}

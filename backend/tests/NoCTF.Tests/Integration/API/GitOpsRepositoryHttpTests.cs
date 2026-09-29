using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.API;

/// <summary>Opt-in cross-repository contract test using real HTTP, JWT and PostgreSQL.</summary>
[Category("Integration")]
public sealed class GitOpsRepositoryHttpTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Repository_apply_reapply_reorder_restore_and_preflight_use_real_api(CancellationToken ct)
    {
        var templateRoot = Environment.GetEnvironmentVariable("NOCTF_GITOPS_TEMPLATE_ROOT");
        if (string.IsNullOrWhiteSpace(templateRoot))
        {
            Skip.Test("Set NOCTF_GITOPS_TEMPLATE_ROOT to the audited challenge-template checkout.");
            return;
        }
        var source = Path.Combine(Path.GetFullPath(templateRoot), ".github", "scripts");
        if (!File.Exists(Path.Combine(source, "repository.cs")))
            throw new DirectoryNotFoundException("Challenge-template scripts were not found.");
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_gitops_http").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var temporary = Path.Combine(Path.GetTempPath(), "noctf-gitops-http", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var scripts = Path.Combine(temporary, ".github", "scripts");
                Directory.CreateDirectory(scripts);
                foreach (var file in Directory.EnumerateFiles(source, "*.cs"))
                    File.Copy(file, Path.Combine(scripts, Path.GetFileName(file)));
                foreach (var folder in new[] { "ISSUE_TEMPLATE", "workflows" })
                {
                    var destination = Path.Combine(temporary, ".github", folder);
                    Directory.CreateDirectory(destination);
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "..", folder), "*.yml"))
                        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
                }

                await using var app = await CreateApplicationAsync(postgres.GetConnectionString(), temporary, ct);
                var competitionId = Guid.NewGuid();
                var botId = Guid.NewGuid();
                var ownerId = Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await db.Database.EnsureCreatedAsync(ct);
                    db.Users.Add(new User
                    {
                        Id = botId, UserName = "gitops-http-bot", NormalizedUserName = "GITOPS-HTTP-BOT",
                        Email = "http-test@bot.invalid", PasswordHash = "not-a-login-secret", Kind = UserKind.Bot,
                        Role = UserRole.Organizer, CreatedAt = now, UpdatedAt = now
                    });
                    db.Users.Add(new User
                    {
                        Id = ownerId, UserName = "competition-owner", NormalizedUserName = "COMPETITION-OWNER",
                        Email = "owner@example.test", PasswordHash = "test-only", Role = UserRole.Administrator,
                        CreatedAt = now, UpdatedAt = now
                    });
                    db.Competitions.Add(new CtfCompetition
                    {
                        Id = competitionId, OwnerId = ownerId, ManagerIds = [botId], Title = "GitOps HTTP contract", Status = CompetitionStatus.Draft, ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                        FlagDerivationSecret = new byte[32], StartAt = now.AddHours(1), EndAt = now.AddHours(2),
                        CreatedAt = now, UpdatedAt = now
                    });
                    await db.SaveChangesAsync(ct);
                }
                var token = app.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                    new AuthenticatedUser(botId, "gitops-http-bot", UserRole.Organizer, UserKind.Bot, 0, false), now).Token;
                var apiUrl = app.Urls.Single();
                var faults = app.Services.GetRequiredService<TestFaults>();
                var first = Guid.NewGuid();
                var second = Guid.NewGuid();
                var firstInstance = Guid.NewGuid();
                var secondInstance = Guid.NewGuid();
                var firstAttachment = Guid.NewGuid();
                var firstFlag = Guid.NewGuid();
                WriteChallenge(temporary, "first", first, firstAttachment, firstFlag);
                WriteChallenge(temporary, "second", second, Guid.NewGuid(), Guid.NewGuid());
                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, swap: false, includeSecond: true);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct, "self-test");

                await RunRepositoryAsync(temporary, apiUrl, token, expectedExit: 0, dryRun: true, ct);
                await Assert.That(await CountChallengesAsync(app, ct)).IsEqualTo(0);
                await Assert.That(faults.MutationRequests).IsEqualTo(0);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await Assert.That(await CountChallengesAsync(app, ct)).IsEqualTo(2);
                var timestamps = await TimestampsAsync(app, ct);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await Assert.That(await TimestampsAsync(app, ct)).IsEquivalentTo(timestamps);

                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, false, true, published: true);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                timestamps = await TimestampsAsync(app, ct);
                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, false, true, published: true, hintContent: "");
                await RunRepositoryAsync(temporary, apiUrl, token, 1, false, ct);
                await Assert.That(await TimestampsAsync(app, ct)).IsEquivalentTo(timestamps);
                await Assert.That(await AllPublishedAsync(app, ct)).IsTrue();
                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, false, true, published: true, hintContent: "Updated hint");
                faults.RejectHintWrites = true;
                await RunRepositoryAsync(temporary, apiUrl, token, 1, false, ct);
                await Assert.That(await AllPublishedAsync(app, ct)).IsTrue();
                await Assert.That(await TimestampsAsync(app, ct)).IsEquivalentTo(timestamps);
                faults.RejectHintWrites = false;
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);

                using (var client = new HttpClient { BaseAddress = new Uri(apiUrl) })
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    foreach (var path in new[]
                    {
                        $"/api/v1/admin/challenges/{first}/attachments/{firstAttachment}",
                        $"/api/v1/admin/challenges/{first}/flags/{firstFlag}",
                        $"/api/v1/admin/competitions/{competitionId}/challenges/{firstInstance}/hints/00000000-0000-0000-0000-000000000144"
                    })
                    {
                        using var deletion = await client.DeleteAsync(path, ct);
                        deletion.EnsureSuccessStatusCode();
                    }
                }
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await Assert.That(await db.ChallengeFlags.IgnoreQueryFilters().CountAsync(ct)).IsEqualTo(2);
                    await Assert.That(await db.ChallengeFlags.Where(item => item.Id == firstFlag)
                        .Select(item => item.DeletedAt).SingleAsync(ct)).IsNull();
                }

                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, swap: true, includeSecond: true);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await Assert.That(await db.CompetitionChallenges.Where(item => item.Id == firstInstance)
                        .Select(item => item.Order).SingleAsync(ct)).IsEqualTo(20);
                    await Assert.That(await db.CompetitionChallenges.Where(item => item.Id == secondInstance)
                        .Select(item => item.Order).SingleAsync(ct)).IsEqualTo(10);
                }

                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, swap: true, includeSecond: false);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await Assert.That(await db.CompetitionChallenges.IgnoreQueryFilters().Where(item => item.Id == secondInstance)
                        .Select(item => item.DeletedAt).SingleAsync(ct)).IsNotNull();
                }
                WriteCompetition(temporary, competitionId, firstInstance, secondInstance, false, true, firstOrder: 10, secondOrder: 30);
                faults.FailAfterRestorePath = $"/api/v1/admin/competitions/{competitionId}/challenges/{secondInstance}/restore";
                await RunRepositoryAsync(temporary, apiUrl, token, 1, false, ct);
                await RunRepositoryAsync(temporary, apiUrl, token, 0, false, ct);
                await Assert.That(await CountChallengesAsync(app, ct)).IsEqualTo(2);
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    await Assert.That(await db.CompetitionChallenges.Where(item => item.Id == firstInstance)
                        .Select(item => item.Order).SingleAsync(ct)).IsEqualTo(10);
                    await Assert.That(await db.CompetitionChallenges.Where(item => item.Id == secondInstance)
                        .Select(item => item.Order).SingleAsync(ct)).IsEqualTo(30);
                }

                timestamps = await TimestampsAsync(app, ct);
                await File.WriteAllTextAsync(Path.Combine(temporary, "web", "first", "attachments", "handout.txt"), "changed bytes", ct);
                await RunRepositoryAsync(temporary, apiUrl, token, 1, false, ct);
                await Assert.That(await TimestampsAsync(app, ct)).IsEquivalentTo(timestamps);
                await File.WriteAllTextAsync(Path.Combine(temporary, "web", "first", "attachments", "handout.txt"), "handout", ct);
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                    var competition = await db.Competitions.SingleAsync(item => item.Id == competitionId, ct);
                    competition.ManagerIds = [];
                    competition.ObserverIds = [botId];
                    await db.SaveChangesAsync(ct);
                }
                await RunRepositoryAsync(temporary, apiUrl, token, 1, false, ct);
                await Assert.That(await TimestampsAsync(app, ct)).IsEquivalentTo(timestamps);
            }
            finally { Directory.Delete(temporary, recursive: true); }
        });
    }

    private static async Task<WebApplication> CreateApplicationAsync(string connection, string temporary, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenApi:Exporting"] = "true", ["Authentication:SigningKey"] = key,
            ["RunnerScoring:SigningKey"] = key, ["Storage:LocalRoot"] = Path.Combine(temporary, "storage"),
            ["Database:Provider"] = "PostgreSql", ["ConnectionStrings:PostgreSql"] = connection
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddNoCtfDatabaseProvider(builder.Configuration);
        builder.Services.AddNoCtfApi(builder.Configuration, includeInfrastructure: true, development: true,
            endpointAssemblies: [typeof(NoCTF.API.Endpoints.Administration.Competitions.GetAdminCompetitionEndpoint).Assembly]);
        builder.Services.AddSingleton(Substitute.For<IBackendMessagePublisher>());
        builder.Services.AddSingleton<TestFaults>();
        builder.Services.AddNoCtfAuthentication(builder.Configuration);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var faults = context.RequestServices.GetRequiredService<TestFaults>();
            var writing = HttpMethods.IsPost(context.Request.Method)
                          || HttpMethods.IsPut(context.Request.Method)
                          || HttpMethods.IsPatch(context.Request.Method)
                          || HttpMethods.IsDelete(context.Request.Method);
            if (writing) Interlocked.Increment(ref faults.MutationRequests);
            if ((writing
                 && faults.RejectHintWrites
                 && context.Request.Path.Value!.Contains("/hints", StringComparison.Ordinal))
                || (HttpMethods.IsPatch(context.Request.Method)
                    && context.Request.Path.Value == faults.FailNextPatchPath))
            {
                faults.FailNextPatchPath = null;
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }
            await next(context);
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Value == faults.FailAfterRestorePath
                && context.Response.StatusCode is >= 200 and < 300)
            {
                faults.FailNextPatchPath = faults.FailAfterRestorePath![..^"/restore".Length];
                faults.FailAfterRestorePath = null;
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync(ct);
        return app;
    }

    private static async Task<int> CountChallengesAsync(WebApplication app, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().Challenges.IgnoreQueryFilters().CountAsync(ct);
    }

    private static async Task<bool> AllPublishedAsync(WebApplication app, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NoCtfDbContext>().CompetitionChallenges.AllAsync(item => item.IsPublished, ct);
    }

    private sealed class TestFaults
    {
        public int MutationRequests;
        public bool RejectHintWrites { get; set; }
        public string? FailAfterRestorePath { get; set; }
        public string? FailNextPatchPath { get; set; }
    }

    private static async Task<DateTimeOffset[]> TimestampsAsync(WebApplication app, CancellationToken ct)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        return [.. await db.Challenges.IgnoreQueryFilters().OrderBy(item => item.Id).Select(item => item.UpdatedAt).ToArrayAsync(ct),
            .. await db.CompetitionChallenges.IgnoreQueryFilters().OrderBy(item => item.Id).Select(item => item.UpdatedAt).ToArrayAsync(ct)];
    }

    private static void WriteChallenge(string root, string slug, Guid id, Guid attachmentId, Guid flagId)
    {
        var directory = Path.Combine(root, "web", slug);
        Directory.CreateDirectory(Path.Combine(directory, "attachments"));
        File.WriteAllText(Path.Combine(directory, "statement.md"), "GitOps contract challenge");
        File.WriteAllText(Path.Combine(directory, "attachments", "handout.txt"), "handout");
        File.WriteAllText(Path.Combine(directory, "challenge.yml"), $$"""
            apiVersion: gitops.noctf.dev/v2
            kind: ChallengeTemplate
            id: {{id}}
            mode: Ctf
            title: {{slug}}
            direction: Web
            visibility: Private
            statement: statement.md
            attachments:
              - id: {{attachmentId}}
                path: attachments/handout.txt
                contentType: text/plain
            flags:
              - id: {{flagId}}
                value: 'flag{gitops-http-test}'
            definition:
              mode: Ctf
              ctf:
                interactionKind: FlagSubmission
            """);
    }

    private static void WriteCompetition(string root, Guid competitionId, Guid first, Guid second, bool swap, bool includeSecond,
        bool published = false, string hintContent = "Contract hint", int? firstOrder = null, int? secondOrder = null)
    {
        static string Entry(Guid id, string slug, int order, bool published, string hintContent) => $$"""
              - id: {{id}}
                challenge: web/{{slug}}
                customTitle: null
                order: {{order}}
                published: {{published.ToString().ToLowerInvariant()}}
                hints: {{(slug == "first" ? "[{id: 00000000-0000-0000-0000-000000000144, content: " + System.Text.Json.JsonSerializer.Serialize(hintContent) + ", cost: 5, publishedAt: null}]" : "[]")}}
                rules:
                  mode: Ctf
                  ctf: {}
            """;
        File.WriteAllText(Path.Combine(root, "competition.yml"), $$"""
            apiVersion: gitops.noctf.dev/v2
            kind: CompetitionChallengeSet
            initialized: true
            competitionId: {{competitionId}}
            mode: Ctf
            challenges:
            """ + "\n" + Entry(first, "first", firstOrder ?? (swap ? 20 : 10), published, hintContent)
            + (includeSecond ? "\n" + Entry(second, "second", secondOrder ?? (swap ? 10 : 20), published, hintContent) : "") + "\n");
    }

    private static async Task RunRepositoryAsync(string root, string apiUrl, string token, int expectedExit, bool dryRun, CancellationToken ct,
        string command = "apply")
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "run", "--file", ".github/scripts/repository.cs", "--", command, "--api-url", apiUrl })
            start.ArgumentList.Add(argument);
        if (dryRun) start.ArgumentList.Add("--dry-run");
        start.Environment["NOCTF_BOT_TOKEN"] = token;
        start.Environment.Remove("GITHUB_STEP_SUMMARY");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the GitOps client.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var text = await output + await error;
        await Assert.That(text).DoesNotContain(token);
        await Assert.That(text).DoesNotContain("flag{gitops-http-test}");
        if (process.ExitCode != expectedExit)
            throw new InvalidOperationException($"GitOps returned {process.ExitCode}, expected {expectedExit}: {text}");
    }
}

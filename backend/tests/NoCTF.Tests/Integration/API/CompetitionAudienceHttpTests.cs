using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.API.Endpoints;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace NoCTF.Tests.Integration.API;

public sealed class CompetitionAudienceHttpTests
{
    [Test]
    [Arguments("Ctf")]
    [Arguments("LiveSolo")]
    [Category("Integration")]
    [NotInParallel]
    [Timeout(300_000)]
    public async Task Draft_competition_exposes_staff_role_only_to_authorized_staff(
        string mode, CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .Build();
        await postgres.StartAsync(cancellationToken);
        await using var redis = new RedisBuilder("redis:7.4.10-alpine3.21@sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2").Build();
        await redis.StartAsync(cancellationToken);
        await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
            .WithCommand("-js")
            .WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
            .Build();
        await nats.StartAsync(cancellationToken);
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
                builder.UseSetting("ConnectionStrings:PostgreSql", postgres.GetConnectionString());
                builder.UseSetting("ConnectionStrings:Redis", redis.GetConnectionString());
                builder.UseSetting("ConnectionStrings:Nats", $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}");
                builder.UseSetting("Hosting:Roles:0", "Api");
                builder.UseSetting("SeedAdmin:UserName", "draft-http-admin");
                builder.UseSetting("SeedAdmin:Email", "draft-http-admin@noctf.local");
                builder.UseSetting("SeedAdmin:Password", "integration-password");
            });
        using var staff = factory.CreateClient();
        using var login = await staff.PostAsJsonAsync("/api/v1/auth/login",
            new { login = "draft-http-admin", password = "integration-password" }, cancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStreamAsync(cancellationToken));
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            loginJson.RootElement.GetProperty("accessToken").GetString());
        var now = DateTimeOffset.UtcNow;
        using var created = await staff.PostAsJsonAsync("/api/v1/admin/competitions", new
        {
            title = $"Draft {mode} HTTP integration", mode,
            startTime = now.AddMinutes(10), endTime = now.AddHours(2),
            teamRegistrationAutoApprove = true, maxTeamMembers = 5,
            maxConcurrentRuntimeInstancesPerTeam = 1, accessMode = "Public"
        }, cancellationToken);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStreamAsync(cancellationToken));
        var competitionId = createdJson.RootElement.GetProperty("id").GetGuid();
        var path = $"/api/v1/competitions/{competitionId}";

        using var anonymous = factory.CreateClient();
        using var forbidden = await anonymous.GetAsync(path, cancellationToken);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        using var detail = await staff.GetAsync(path, cancellationToken);
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(detail.Headers.CacheControl?.NoStore).IsTrue();
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStreamAsync(cancellationToken));
        await Assert.That(detailJson.RootElement.GetProperty("status").GetString()).IsEqualTo("Draft");
        await Assert.That(detailJson.RootElement.GetProperty("mode").GetString()).IsEqualTo(mode);
        await Assert.That(detailJson.RootElement.GetProperty("administrationRole").GetString()).IsEqualTo("Owner");
        if (mode == "LiveSolo")
        {
            using var settings = await staff.GetAsync($"{path}/live-solo/configuration", cancellationToken);
            await Assert.That(settings.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        string[] roles = ["Owner", "Manager", "Judge", "Observer", "Participant"];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var competition = await db.Competitions.SingleAsync(x => x.Id == competitionId, cancellationToken);
            foreach (var role in roles)
            {
                var actor = new User
                {
                    Id = Guid.CreateVersion7(), UserName = $"draft-{role}", NormalizedUserName = $"DRAFT-{role.ToUpperInvariant()}",
                    Email = $"draft-{role}@example.test", NormalizedEmail = $"DRAFT-{role.ToUpperInvariant()}@EXAMPLE.TEST",
                    Kind = UserKind.Human, Role = UserRole.User, AccountStatus = UserAccountStatus.Active,
                    EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
                };
                actor.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()
                    .HashPassword(actor, "integration-password");
                db.Users.Add(actor);
                if (role == "Owner") competition.OwnerId = actor.Id;
                else if (Enum.TryParse<CompetitionCollaboratorRole>(role, out var collaboratorRole))
                    competition.Collaborators.Add(new() { CompetitionId = competitionId, UserId = actor.Id, Role = collaboratorRole });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        foreach (var role in roles)
        {
            using var actor = factory.CreateClient();
            using var actorLogin = await actor.PostAsJsonAsync("/api/v1/auth/login",
                new { login = $"draft-{role}", password = "integration-password" }, cancellationToken);
            await Assert.That(actorLogin.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var actorLoginJson = JsonDocument.Parse(await actorLogin.Content.ReadAsStreamAsync(cancellationToken));
            actor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                actorLoginJson.RootElement.GetProperty("accessToken").GetString());
            using var actorDetail = await actor.GetAsync(path, cancellationToken);
            await Assert.That(actorDetail.StatusCode).IsEqualTo(role == "Participant" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            if (role != "Participant")
            {
                using var actorJson = JsonDocument.Parse(await actorDetail.Content.ReadAsStreamAsync(cancellationToken));
                await Assert.That(actorJson.RootElement.GetProperty("administrationRole").GetString()).IsEqualTo(role);
            }
        }

        using var stillForbidden = await anonymous.GetAsync(path, cancellationToken);
        await Assert.That(stillForbidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Category("Integration")]
    [NotInParallel]
    [Timeout(300_000)]
    public async Task Hidden_competition_is_absent_from_catalog_and_rejects_known_routes(
        CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase("noctf_hidden_http")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:PostgreSql", postgres.GetConnectionString());
                builder.UseSetting("SeedAdmin:UserName", "hidden-http-admin");
                builder.UseSetting("SeedAdmin:Email", "hidden-http-admin@noctf.local");
                builder.UseSetting("SeedAdmin:Password", "integration-password");
            });
        using var administrator = factory.CreateClient();

        using var login = await administrator.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { login = "hidden-http-admin", password = "integration-password" },
            cancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var loginJson = JsonDocument.Parse(
            await login.Content.ReadAsStreamAsync(cancellationToken));
        administrator.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginJson.RootElement.GetProperty("accessToken").GetString());

        var now = DateTimeOffset.UtcNow;
        using var created = await administrator.PostAsJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = "Hidden HTTP integration",
                mode = "Ctf",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(2),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 0,
                accessMode = "StaffOnly"
            },
            cancellationToken);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(
            await created.Content.ReadAsStreamAsync(cancellationToken));
        var competitionId = createdJson.RootElement.GetProperty("id").GetGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var competition = await db.Competitions.SingleAsync(
                item => item.Id == competitionId,
                cancellationToken);
            competition.Status = CompetitionStatus.Published;
            await db.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<CompetitionReadModelCache>()
                .InvalidateAsync(competitionId, cancellationToken);
        }

        using var anonymous = factory.CreateClient();
        using var catalog = await anonymous.GetAsync(
            "/api/v1/competitions",
            cancellationToken);
        await Assert.That(catalog.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var catalogJson = JsonDocument.Parse(
            await catalog.Content.ReadAsStreamAsync(cancellationToken));
        await Assert.That(catalogJson.RootElement.GetProperty("items")
                .EnumerateArray()
                .Any(item => item.GetProperty("id").GetGuid() == competitionId))
            .IsFalse();

        var challengeId = Guid.CreateVersion7();
        (HttpMethod Method, string Path)[] blockedRequests =
        [
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}"),
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}/poster"),
            (HttpMethod.Get, $"/api/v1/competitions/{competitionId}/leaderboard"),
            (HttpMethod.Post,
                $"/api/v1/competitions/{competitionId}/challenges/{challengeId}/flag-submissions")
        ];
        foreach (var blockedRequest in blockedRequests)
        {
            using var request = new HttpRequestMessage(
                blockedRequest.Method,
                blockedRequest.Path);
            if (blockedRequest.Method == HttpMethod.Post)
                request.Content = JsonContent.Create(new { flag = "flag{hidden}" });
            using var response = await anonymous.SendAsync(request, cancellationToken);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        }

        using var staffDetail = await administrator.GetAsync(
            $"/api/v1/competitions/{competitionId}",
            cancellationToken);
        await Assert.That(staffDetail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(staffDetail.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(staffDetail.Headers.CacheControl?.NoStore).IsTrue();
        using var staffDetailJson = JsonDocument.Parse(
            await staffDetail.Content.ReadAsStreamAsync(cancellationToken));
        await Assert.That(staffDetailJson.RootElement.GetProperty("accessMode").GetString())
            .IsEqualTo("StaffOnly");
    }
}

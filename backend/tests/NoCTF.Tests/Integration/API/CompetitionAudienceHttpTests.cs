using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Endpoints;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.API;

public sealed class CompetitionAudienceHttpTests
{
    [Test]
    [Category("DevelopmentIntegration")]
    public async Task Hidden_competition_is_absent_from_catalog_and_rejects_known_routes(
        CancellationToken cancellationToken)
    {
        var databaseName = $"noctf-hidden-http-{Guid.NewGuid():N}";
        using var factory = new WebApplicationFactory<HealthEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Development:DatabaseName", databaseName);
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

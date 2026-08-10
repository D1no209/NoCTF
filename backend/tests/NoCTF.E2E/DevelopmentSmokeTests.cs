using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Endpoints;
using NoCTF.Application.Messaging;
using Wolverine;

namespace NoCTF.E2E;

[Category("DevelopmentE2E")]
public sealed class DevelopmentSmokeTests
{
    [Test]
    public async Task Development_API_supports_health_login_create_and_list(
        CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("NOCTF_E2E_BASE_URL");
        var password = Environment.GetEnvironmentVariable("NOCTF_E2E_ADMIN_PASSWORD")
            ?? "dev-admin-change-me";
        var userName = Environment.GetEnvironmentVariable("NOCTF_E2E_ADMIN_USER")
            ?? "dev-admin";
        using var factory = baseUrl is null
            ? new WebApplicationFactory<HealthEndpoint>().WithWebHostBuilder(builder =>
                builder.UseEnvironment("Development"))
            : null;
        using var client = factory?.CreateClient()
            ?? new HttpClient { BaseAddress = new Uri(baseUrl!) };

        using var health = await client.GetAsync("/health", cancellationToken);
        await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { login = userName, password },
            cancellationToken);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var loginJson = JsonDocument.Parse(
            await login.Content.ReadAsStreamAsync(cancellationToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginJson.RootElement.GetProperty("accessToken").GetString());

        var now = DateTimeOffset.UtcNow;
        using var created = await client.PostAsJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = $"Development E2E {Guid.NewGuid():N}",
                description = "Container-free development boundary",
                mode = "Ctf",
                startTime = now.AddMinutes(10),
                endTime = now.AddHours(2),
                teamRegistrationAutoApprove = true,
                maxTeamMembers = 5,
                maxConcurrentRuntimeInstancesPerTeam = 0
            },
            cancellationToken);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var createdJson = JsonDocument.Parse(
            await created.Content.ReadAsStreamAsync(cancellationToken));
        var competitionId = createdJson.RootElement.GetProperty("id").GetGuid();

        if (factory is not null)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync(new ProjectLeaderboard(competitionId), cancellationToken);
        }

        using var listed = await client.GetAsync(
            "/api/v1/admin/competitions",
            cancellationToken);
        await Assert.That(listed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var listJson = JsonDocument.Parse(
            await listed.Content.ReadAsStreamAsync(cancellationToken));
        await Assert.That(listJson.RootElement.GetProperty("items").GetArrayLength())
            .IsGreaterThan(0);

        HttpStatusCode leaderboardStatus = 0;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var leaderboard = await client.GetAsync(
                $"/api/v1/competitions/{competitionId}/leaderboard",
                cancellationToken);
            leaderboardStatus = leaderboard.StatusCode;
            if (leaderboardStatus == HttpStatusCode.OK)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        await Assert.That(leaderboardStatus).IsEqualTo(HttpStatusCode.OK);
    }
}

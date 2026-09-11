using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class HumanVerificationOpenApiTests
{
    [Test]
    public async Task Verification_runs_after_auth_rate_admission_and_email_gates()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            BackendRoot(),
            "src", "NoCTF.API", "Composition", "PipelineConfiguration.cs"));
        var authentication = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var rateLimiter = source.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var authorization = source.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);
        var admission = source.IndexOf("Security.RequestAdmissionMiddleware", StringComparison.Ordinal);
        var email = source.IndexOf("Security.EmailVerificationGateMiddleware", StringComparison.Ordinal);
        var verification = source.IndexOf("Security.HumanVerificationMiddleware", StringComparison.Ordinal);

        await Assert.That(authentication).IsGreaterThanOrEqualTo(0);
        await Assert.That(authentication).IsLessThan(rateLimiter);
        await Assert.That(rateLimiter).IsLessThan(authorization);
        await Assert.That(authorization).IsLessThan(admission);
        await Assert.That(admission).IsLessThan(email);
        await Assert.That(email).IsLessThan(verification);
    }

    [Test]
    public async Task Only_selected_player_operations_publish_the_verification_header()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(OpenApiPath()));
        var protectedPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.NameEquals("parameters"))
                    continue;
                if (!operation.Value.TryGetProperty("parameters", out var parameters))
                    continue;
                var verificationParameter = parameters.EnumerateArray().FirstOrDefault(parameter =>
                        parameter.TryGetProperty("name", out var name)
                        && name.GetString() == "X-NoCTF-Human-Verification");
                if (verificationParameter.ValueKind == JsonValueKind.Undefined)
                    continue;

                protectedPaths.Add(path.Name);
                await Assert.That(verificationParameter.TryGetProperty("required", out _)).IsFalse();
                await Assert.That(verificationParameter.GetProperty("schema").GetProperty("type").GetString())
                    .IsEqualTo("string");
                var responses = operation.Value.GetProperty("responses");
                foreach (var status in new[] { "403", "503" })
                {
                    await Assert.That(responses.TryGetProperty(status, out var response)).IsTrue();
                    await Assert.That(response.GetProperty("content")
                        .TryGetProperty("application/problem+json", out _)).IsTrue();
                }
            }
        }

        await Assert.That(protectedPaths).IsEquivalentTo(new HashSet<string>(StringComparer.Ordinal)
        {
            "/api/v1/auth/login",
            "/api/v1/auth/register",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-break-flag-judgement",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets",
            "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets/{runtimeInstanceId}/fix"
        });
        await Assert.That(protectedPaths.Any(path => path.Contains("/admin/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(protectedPaths.Any(path => path.Contains("/internal/", StringComparison.Ordinal))).IsFalse();
    }

    private static string OpenApiPath() => Path.Combine(
        BackendRoot(),
        "src", "NoCTF.API", "wwwroot", "openapi", "v1.json");

    private static string BackendRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", ".."));
}

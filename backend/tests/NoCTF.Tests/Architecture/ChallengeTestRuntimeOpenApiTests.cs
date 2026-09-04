using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class ChallengeTestRuntimeOpenApiTests
{
    private const string Prefix = "/api/v1/admin/challenges/{challengeId}/test-runtime";

    [Test]
    public async Task ChallengeTestRuntime_ExposesTypedLifecycleAndProtectedFlagStatus()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var paths = root.GetProperty("paths");

        await Assert.That(paths.GetProperty(Prefix)
                .GetProperty("get")
                .GetProperty("responses")
                .EnumerateObject()
                .Select(response => response.Name)
                .ToArray())
            .IsEquivalentTo(["200", "401", "403", "404"]);
        foreach (var action in new[] { "start", "reset", "stop" })
        {
            await Assert.That(paths.GetProperty($"{Prefix}/{action}")
                    .GetProperty("post")
                    .GetProperty("responses")
                    .EnumerateObject()
                    .Select(response => response.Name)
                    .ToArray())
                .IsEquivalentTo(["202", "401", "403", "404", "409", "503"]);
        }
        await Assert.That(paths.GetProperty($"{Prefix}/extend")
                .GetProperty("post")
                .GetProperty("responses")
                .EnumerateObject()
                .Select(response => response.Name)
                .ToArray())
            .IsEquivalentTo(["202", "400", "401", "403", "404", "409", "503"]);

        var schema = root.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("NoCTFAPIEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse")
            .GetProperty("properties");
        await Assert.That(schema.TryGetProperty("testFlag", out _)).IsTrue();
        await Assert.That(schema.TryGetProperty("flagState", out _)).IsTrue();
        await Assert.That(schema.TryGetProperty("urls", out _)).IsTrue();
    }

    [Test]
    public async Task PlatformRuntimeAdministration_CoversCompetitionAndChallengeTestScopes()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var paths = root.GetProperty("paths");
        await Assert.That(paths.TryGetProperty(
                "/api/v1/admin/platform/runtimes/{runtimeInstanceId}/terminate",
                out _))
            .IsTrue();
        await Assert.That(paths.TryGetProperty(
                "/api/v1/admin/platform/runtimes/{runtimeInstanceId}/force-terminate",
                out _))
            .IsTrue();
        var scope = root.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("NoCTFAPIEndpointsAdministrationPlatformPlatformRuntimeScopeProtocol");
        await Assert.That(scope.GetProperty("enum")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray())
            .IsEquivalentTo(["Competition", "ChallengeTest"]);
    }

    private static async Task<JsonDocument> ReadSwaggerAsync()
    {
        var backend = FindBackendRoot();
        return JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend, "artifacts", "openapi", "swagger.json")));
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Backend root was not found.");
    }
}

using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class AwdpCheckResultOpenApiTests
{
    [Test]
    public async Task Awdp_check_result_outcome_is_documented_as_the_three_public_strings()
    {
        using var swagger = await ReadSwaggerAsync();
        var schema = swagger.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("NoCTFAPIEndpointsInternalAwdpFixResultOutcome");

        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray())
            .IsEquivalentTo([
                "ExploitSucceeded",
                "DefenseSucceeded",
                "ServiceAbnormal"
            ]);
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

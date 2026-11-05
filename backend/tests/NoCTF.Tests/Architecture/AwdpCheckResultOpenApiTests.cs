using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class AwdpCheckResultOpenApiTests
{
    [Test]
    public async Task Fix_check_callbacks_require_explicit_probe_identity_and_result()
    {
        using var swagger = await ReadSwaggerAsync();
        var schemas = swagger.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var request = schemas.GetProperty("NoCTFAPIEndpointsInternalRecordCheckResultRequest");
        await Assert.That(request.GetProperty("properties").TryGetProperty("outcome", out _)).IsFalse();
        var probe = schemas.GetProperty("NoCTFAPIEndpointsInternalCheckProbeEvidenceRequest");
        await Assert.That(probe.GetProperty("required").EnumerateArray().Select(value => value.GetString())).Contains("kind");
        await Assert.That(probe.GetProperty("required").EnumerateArray().Select(value => value.GetString())).Contains("result");
        var kinds = schemas.GetProperty("NoCTFDomainChallengesCapabilitiesCheckItemKind");
        await Assert.That(kinds.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(kinds.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)).IsEquivalentTo(new[]
            { "Connection", "Functionality", "Vulnerability", "FlagWrite", "CurrentFlagRead", "HistoricalFlagRead" });
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

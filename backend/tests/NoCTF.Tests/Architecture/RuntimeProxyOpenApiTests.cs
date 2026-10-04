using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class RuntimeProxyOpenApiTests
{
    [Test]
    public async Task Proxy_probe_and_capture_administration_are_typed()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(FindBackendRoot(), "artifacts", "openapi", "v1.json")));
        var paths = document.RootElement.GetProperty("paths");
        var probe = paths.GetProperty(
            "/api/v1/runtime-proxies/{runtimeInstanceId}/{bindingIndex}")
            .GetProperty("get");
        await Assert.That(probe.GetProperty("responses").EnumerateObject()
                .Select(response => response.Name).ToArray())
            .IsEquivalentTo(["204", "404"]);
        await Assert.That(probe.TryGetProperty("security", out var security)
                && security.GetArrayLength() > 0)
            .IsFalse();

        var list = paths.GetProperty(
            "/api/v1/admin/competitions/{competitionId}/traffic-captures")
            .GetProperty("get");
        await Assert.That(list.GetProperty("security").EnumerateArray()
            .Any(requirement => requirement.TryGetProperty("Bearer", out _))).IsTrue();
        foreach (var route in new[]
                 {
                     "/api/v1/admin/competitions/{competitionId}/traffic-captures/{runtimeInstanceId}/file",
                     "/api/v1/admin/competitions/{competitionId}/traffic-captures/export"
                 })
        {
            var operation = paths.GetProperty(route).EnumerateObject()
                .Single(item => item.Name is "get" or "post").Value;
            var content = operation.GetProperty("responses").GetProperty("200")
                .GetProperty("content");
            await Assert.That(content.EnumerateObject().Any()).IsTrue();
        }
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

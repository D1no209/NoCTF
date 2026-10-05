using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class OpenApiMetadataTests
{
    [Test]
    public async Task Every_operation_has_unique_id_summary_and_description()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(OpenApiPath()));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.NameEquals("parameters"))
                    continue;
                var operation = method.Value;
                var operationId = operation.GetProperty("operationId").GetString();
                await Assert.That(string.IsNullOrWhiteSpace(operationId)).IsFalse();
                await Assert.That(ids.Add(operationId!)).IsTrue();
                await Assert.That(string.IsNullOrWhiteSpace(
                    operation.GetProperty("summary").GetString())).IsFalse();
                await Assert.That(string.IsNullOrWhiteSpace(
                    operation.GetProperty("description").GetString())).IsFalse();
            }
        }
    }

    private static string OpenApiPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "src", "NoCTF.API", "wwwroot", "openapi", "v1.json"));
}

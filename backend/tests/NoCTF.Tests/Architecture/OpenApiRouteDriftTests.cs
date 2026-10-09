using System.Text.RegularExpressions;

namespace NoCTF.Tests.Architecture;

public partial class OpenApiRouteDriftTests
{
    [Test]
    public async Task ExportedOpenApi_ExactlyMatchesDocumentedV1Routes()
    {
        var backend = FindBackendRoot();
        var swaggerPath = Path.Combine(backend, "artifacts", "openapi", "swagger.json");
        var apiDocumentPath = Path.GetFullPath(Path.Combine(backend, "..", "specs", "api.md"));
        await Assert.That(File.Exists(swaggerPath)).IsTrue();
        await Assert.That(File.Exists(apiDocumentPath)).IsTrue();

        using var swagger = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(swaggerPath));
        var actual = swagger.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .Where(operation => operation != "GET /health")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = (await File.ReadAllLinesAsync(apiDocumentPath))
            .Select(line => RouteLine().Match(line))
            .Where(match => match.Success)
            .Select(match =>
                $"{match.Groups[1].Value} {match.Groups[2].Value}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(actual).Count().IsEqualTo(325);

        var operationIds = swagger.RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(operation => HttpMethods.Contains(operation.Name))
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        await Assert.That(operationIds).DoesNotContain((string?)null);
        await Assert.That(operationIds.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(operationIds.Length);
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

    private static readonly HashSet<string> HttpMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "get", "post", "put", "patch", "delete"
        };

    [GeneratedRegex(@"^(GET|POST|PUT|PATCH|DELETE)\s+(/\S+)\s*$")]
    private static partial Regex RouteLine();
}

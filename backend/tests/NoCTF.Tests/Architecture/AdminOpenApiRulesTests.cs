using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public class AdminOpenApiRulesTests
{
    [Test]
    public async Task Admin_endpoints_publish_complete_stable_metadata()
    {
        var backend = FindBackendRoot();
        using var swagger = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend, "artifacts", "openapi", "swagger.json")));

        var operations = swagger.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/v1/admin/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => new AdminOperation(
                    $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    operation.Value)))
            .ToArray();

        await Assert.That(operations).Count().IsEqualTo(85);
        await Assert.That(operations
            .Where(operation => !operation.Value.TryGetProperty("operationId", out var id)
                || id.GetString() is not { } value
                || !value.StartsWith("Admin", StringComparison.Ordinal))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Select(operation => operation.Value.GetProperty("operationId").GetString())
            .Distinct(StringComparer.Ordinal)
            .Count()).IsEqualTo(operations.Length);
        await Assert.That(operations
            .Where(operation => MissingText(operation.Value, "summary"))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => MissingText(operation.Value, "description"))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => !operation.Value.TryGetProperty("tags", out var tags)
                || tags.GetArrayLength() == 0)
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => !operation.Value.TryGetProperty("security", out var security)
                || !security.EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _)))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => operation.Value.GetProperty("responses").TryGetProperty("422", out _))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => operation.Value.GetProperty("responses").TryGetProperty("400", out var badRequest)
                && (!badRequest.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("application/problem+json", out _)))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => operation.Value.GetProperty("responses").TryGetProperty("409", out var conflict)
                && (!conflict.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("application/problem+json", out _)))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
    }

    private static bool MissingText(JsonElement operation, string propertyName) =>
        !operation.TryGetProperty(propertyName, out var value)
        || string.IsNullOrWhiteSpace(value.GetString());

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

    private sealed record AdminOperation(string Route, JsonElement Value);

    private static readonly HashSet<string> HttpMethods =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "get", "post", "put", "patch", "delete"
        };
}

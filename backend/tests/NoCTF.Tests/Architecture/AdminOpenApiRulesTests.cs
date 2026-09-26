using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public class AdminOpenApiRulesTests
{
    [Test]
    public async Task Patch_download_contract_declares_binary_bytes_and_authenticated_error_responses()
    {
        using var swagger = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(FindBackendRoot(), "artifacts", "openapi", "swagger.json")));
        var operation = swagger.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/patch").GetProperty("get");
        var responses = operation.GetProperty("responses");
        var body = responses.GetProperty("200").GetProperty("content").GetProperty("application/octet-stream").GetProperty("schema");
        await Assert.That(body.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(body.GetProperty("format").GetString()).IsEqualTo("binary");
        foreach (var status in new[] { "403", "404", "409", "503" })
            await Assert.That(responses.GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _)).IsTrue();
        await Assert.That(operation.GetProperty("security").EnumerateArray().Any(x => x.TryGetProperty("Bearer", out _))).IsTrue();
    }

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

        await Assert.That(operations).Count().IsEqualTo(127);
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
                && !HasValidationProblemContent(badRequest))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
        await Assert.That(operations
            .Where(operation => operation.Value.GetProperty("responses").TryGetProperty("409", out var conflict)
                && !HasConflictContent(conflict))
            .Select(operation => operation.Route)
            .ToArray()).IsEmpty();
    }

    [Test]
    public async Task Competition_runtime_quota_is_a_required_non_nullable_contract()
    {
        var backend = FindBackendRoot();
        using var swagger = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend, "artifacts", "openapi", "swagger.json")));
        var schemas = swagger.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        foreach (var schemaName in new[]
                 {
                     "NoCTFAPIEndpointsAdministrationCompetitionsCreateCompetitionRequest",
                     "NoCTFAPIEndpointsAdministrationCompetitionsCompetitionMetadataPatchRequest"
                 })
        {
            var schema = schemas.GetProperty(schemaName);
            await Assert.That(schema.GetProperty("required")
                    .EnumerateArray()
                    .Any(property => property.GetString()
                        == "maxConcurrentRuntimeInstancesPerTeam"))
                .IsTrue();
            await AssertQuotaPropertyAsync(schema);
        }

        await AssertQuotaPropertyAsync(schemas.GetProperty(
            "NoCTFAPIEndpointsCompetitionsCompetitionResponse"));
    }

    private static async Task AssertQuotaPropertyAsync(JsonElement schema)
    {
        var property = schema.GetProperty("properties")
            .GetProperty("maxConcurrentRuntimeInstancesPerTeam");
        await Assert.That(property.GetProperty("type").GetString())
            .IsEqualTo("integer");
        await Assert.That(property.GetProperty("format").GetString())
            .IsEqualTo("int32");
        var isNullable = property.TryGetProperty("nullable", out var nullable)
            && nullable.GetBoolean();
        await Assert.That(isNullable).IsFalse();
    }

    private static bool HasConflictContent(JsonElement response) =>
        response.TryGetProperty("content", out var content)
        && (content.TryGetProperty("application/problem+json", out _)
            || content.TryGetProperty("application/json", out _));

    private static bool HasValidationProblemContent(JsonElement response) =>
        response.TryGetProperty("content", out var content)
        && content.TryGetProperty("application/problem+json", out var problem)
        && problem.TryGetProperty("schema", out var schema)
        && schema.TryGetProperty("$ref", out var reference)
        && reference.GetString()!.EndsWith(
            "ValidationProblemDetails",
            StringComparison.Ordinal);

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

using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class CompetitionPermissionsOpenApiTests
{
    private const string PermissionsPath =
        "/api/v1/admin/competitions/{competitionId}/permissions";
    private const string CandidatesPath =
        "/api/v1/admin/competitions/{competitionId}/permission-candidates";

    [Test]
    public async Task Permission_reads_are_bearer_protected_typed_admin_contracts()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        foreach (var path in new[] { PermissionsPath, CandidatesPath })
        {
            var operation = Operation(root, path, "get");
            var operationId = operation.GetProperty("operationId").GetString();
            await Assert.That(operationId is not null
                && operationId.StartsWith("Admin", StringComparison.Ordinal))
                .IsTrue();
            await Assert.That(operation.GetProperty("security")
                .EnumerateArray()
                .Any(requirement => requirement.TryGetProperty("Bearer", out _)))
                .IsTrue();
            var responses = operation.GetProperty("responses");
            await Assert.That(responses.TryGetProperty("200", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("401", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("403", out _)).IsTrue();
            await Assert.That(responses.TryGetProperty("404", out _)).IsTrue();
        }
    }

    [Test]
    public async Task Permission_schemas_are_complete_minimal_and_revision_fenced()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        var permissions = ResponseSchema(
            root,
            Operation(root, PermissionsPath, "get"),
            "200");
        await Assert.That(PropertyNames(permissions))
            .IsEquivalentTo([
                "competitionId",
                "ownerId",
                "managerIds",
                "judgeIds",
                "observerIds",
                "permissionRevision"
            ]);
        AssertGuid(permissions, "competitionId");
        AssertGuid(permissions, "ownerId");
        AssertGuidArray(permissions, "managerIds");
        AssertGuidArray(permissions, "judgeIds");
        AssertGuidArray(permissions, "observerIds");
        var permissionRevision = permissions.GetProperty("properties")
            .GetProperty("permissionRevision");
        await Assert.That(permissionRevision.GetProperty("type").GetString())
            .IsEqualTo("integer");
        await Assert.That(permissionRevision.GetProperty("format").GetString())
            .IsEqualTo("int32");

        var candidates = ResponseSchema(
            root,
            Operation(root, CandidatesPath, "get"),
            "200");
        var candidateItems = ResolveSchema(
            root,
            candidates.GetProperty("properties")
                .GetProperty("items")
                .GetProperty("items"));
        await Assert.That(PropertyNames(candidateItems))
            .IsEquivalentTo(["id", "userName", "kind", "role", "emailVerified"]);
        AssertGuid(candidateItems, "id");
        await Assert.That(candidateItems.GetProperty("properties")
            .GetProperty("userName")
            .GetProperty("type")
            .GetString()).IsEqualTo("string");
        await Assert.That(candidateItems.GetProperty("properties")
            .GetProperty("emailVerified")
            .GetProperty("type")
            .GetString()).IsEqualTo("boolean");

        var update = Operation(root, PermissionsPath, "put");
        var request = ResolveSchema(
            root,
            update.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        await Assert.That(request.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Contains("expectedPermissionRevision", StringComparer.Ordinal))
            .IsTrue();
        var expectedRevision = request.GetProperty("properties")
            .GetProperty("expectedPermissionRevision");
        await Assert.That(expectedRevision.GetProperty("type").GetString())
            .IsEqualTo("integer");
        await Assert.That(expectedRevision.GetProperty("format").GetString())
            .IsEqualTo("int32");
        await Assert.That(expectedRevision.GetProperty("minimum").GetDouble())
            .IsEqualTo(0);

        var conflict = ResponseSchema(root, update, "409");
        var conflictCode = ResolveSchema(
            root,
            conflict.GetProperty("properties").GetProperty("code"));
        var conflictValues = conflictCode.GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        await Assert.That(conflictValues).Contains("RevisionConflict");
        await Assert.That(conflictValues).Contains("EmailNotVerified");
    }

    [Test]
    public async Task Public_competition_schema_does_not_expose_permission_state()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var publicCompetition = ResponseSchema(
            root,
            Operation(root, "/api/v1/competitions/{competitionId}", "get"),
            "200");
        var properties = PropertyNames(publicCompetition);

        await Assert.That(properties).DoesNotContain("managerIds");
        await Assert.That(properties).DoesNotContain("judgeIds");
        await Assert.That(properties).DoesNotContain("observerIds");
        await Assert.That(properties).DoesNotContain("permissionRevision");
    }

    private static JsonElement Operation(
        JsonElement root,
        string path,
        string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static JsonElement ResponseSchema(
        JsonElement root,
        JsonElement operation,
        string statusCode)
    {
        var content = operation.GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content");
        var mediaType = content.TryGetProperty("application/json", out var json)
            ? json
            : content.GetProperty("application/problem+json");
        return ResolveSchema(root, mediaType.GetProperty("schema"));
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            schema = root.GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(name);
        }
        return schema;
    }

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

    private static void AssertGuid(JsonElement schema, string propertyName)
    {
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        if (property.GetProperty("type").GetString() != "string"
            || property.GetProperty("format").GetString() != "guid")
            throw new InvalidOperationException($"{propertyName} must be a Guid.");
    }

    private static void AssertGuidArray(JsonElement schema, string propertyName)
    {
        var property = schema.GetProperty("properties").GetProperty(propertyName);
        if (property.GetProperty("type").GetString() != "array")
            throw new InvalidOperationException($"{propertyName} must be an array.");
        var item = property.GetProperty("items");
        if (item.GetProperty("type").GetString() != "string"
            || item.GetProperty("format").GetString() != "guid")
            throw new InvalidOperationException($"{propertyName} must contain Guid values.");
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

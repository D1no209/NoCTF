using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class ChallengeTemplateUpdateOpenApiTests
{
    private const string ItemPath = "/api/v1/admin/challenges/{challengeId}";

    [Test]
    public async Task Update_uses_typed_conflict_with_stable_required_shape()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var operation = Operation(root);
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var response = ResolveSchema(root, ResponseSchema(operation, "409"));
        var code = ResolveSchema(
            root,
            response.GetProperty("properties").GetProperty("code"));

        await Assert.That(operation.GetProperty("responses").TryGetProperty(
            "404",
            out _)).IsTrue();
        await Assert.That(ReferenceName(ResponseSchema(operation, "400")).EndsWith(
            "ValidationProblemDetails",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(ReferenceName(ResponseSchema(operation, "409")).EndsWith(
            "ChallengeTemplateConflictResponse",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(RequiredPropertyNames(response))
            .IsEquivalentTo(["code", "userIds"]);
        await Assert.That(code.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(code.GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray())
            .IsEquivalentTo([
                "ResourceIdConflict",
                "RevisionConflict",
                "ActiveCompetitionModeConflict",
                "OwnerIncludedInManagerSet",
                "UserNotFound",
                "RoleNotEligible"
            ]);
        await Assert.That(schemas.EnumerateObject().Count(schema =>
            schema.Name.EndsWith(
                "ChallengeTemplateConflictCode",
                StringComparison.Ordinal))).IsEqualTo(1);
    }

    [Test]
    public async Task Update_requires_complete_revision_fenced_payload()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var requestBody = Operation(root).GetProperty("requestBody");
        var request = ResolveSchema(
            root,
            requestBody
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(requestBody.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(PropertyNames(request))
            .IsEquivalentTo([
                "mode",
                "visibility",
                "title",
                "description",
                "direction",
                "definitionJson",
                "expectedRevision"
            ]);
        await Assert.That(RequiredPropertyNames(request))
            .IsEquivalentTo([
                "mode",
                "visibility",
                "title",
                "direction",
                "expectedRevision"
            ]);
        AssertNonNegativeInt32(
            request.GetProperty("properties").GetProperty("expectedRevision"));
        await AssertNamedStringEnumAsync(
            root,
            request.GetProperty("properties").GetProperty("mode"),
            ["Ctf", "Awd", "Awdp", "Koh"]);
        await AssertNamedStringEnumAsync(
            root,
            request.GetProperty("properties").GetProperty("visibility"),
            ["Private", "Shared"]);
    }

    private static JsonElement Operation(JsonElement root) =>
        root.GetProperty("paths").GetProperty(ItemPath).GetProperty("put");

    private static JsonElement ResponseSchema(JsonElement operation, string statusCode)
    {
        var content = operation.GetProperty("responses")
            .GetProperty(statusCode)
            .GetProperty("content");
        var mediaType = content.TryGetProperty("application/json", out var json)
            ? json
            : content.GetProperty("application/problem+json");
        return mediaType.GetProperty("schema");
    }

    private static string ReferenceName(JsonElement schema) =>
        schema.GetProperty("$ref").GetString()!.Split('/')[^1];

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

    private static string[] RequiredPropertyNames(JsonElement schema) =>
        schema.GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString()!)
            .ToArray();

    private static void AssertNonNegativeInt32(JsonElement schema)
    {
        if (schema.GetProperty("type").GetString() != "integer"
            || schema.GetProperty("format").GetString() != "int32"
            || schema.GetProperty("minimum").GetDouble() != 0)
        {
            throw new InvalidOperationException(
                "ExpectedRevision must be a non-negative required Int32.");
        }
    }

    private static async Task AssertNamedStringEnumAsync(
        JsonElement root,
        JsonElement schema,
        string[] expectedValues)
    {
        if (schema.TryGetProperty("oneOf", out var variants))
            schema = variants.EnumerateArray().Single();
        schema = ResolveSchema(root, schema);
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(schema.GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray())
            .IsEquivalentTo(expectedValues);
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

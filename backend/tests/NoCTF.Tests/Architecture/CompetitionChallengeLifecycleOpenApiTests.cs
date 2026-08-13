using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class CompetitionChallengeLifecycleOpenApiTests
{
    private const string CollectionPath =
        "/api/v1/admin/competitions/{competitionId}/challenges";
    private const string ItemPath =
        "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}";
    private const string RestorePath =
        "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore";

    [Test]
    public async Task Competition_challenge_mutations_share_typed_conflict_schema()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        foreach (var operation in new[]
        {
            Operation(root, CollectionPath, "post"),
            Operation(root, ItemPath, "put"),
            Operation(root, ItemPath, "delete"),
            Operation(root, RestorePath, "post")
        })
        {
            var schema = ResponseSchema(operation, "409");
            await Assert.That(ReferenceName(schema).EndsWith(
                "CompetitionChallengeConflictResponse",
                StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task Competition_challenge_conflict_codes_are_stable_named_values()
    {
        using var swagger = await ReadSwaggerAsync();
        var schemas = swagger.RootElement.GetProperty("components").GetProperty("schemas");
        var responseSchema = schemas.EnumerateObject()
            .Single(schema => schema.Name.EndsWith(
                "CompetitionChallengeConflictResponse",
                StringComparison.Ordinal))
            .Value;
        var codeSchema = schemas.EnumerateObject()
            .Single(schema => schema.Name.EndsWith(
                "CompetitionChallengeConflictCode",
                StringComparison.Ordinal))
            .Value;

        await Assert.That(RequiredPropertyNames(responseSchema))
            .IsEquivalentTo(["code"]);
        await Assert.That(codeSchema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(codeSchema.GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray())
            .IsEquivalentTo([
                "ResourceIdConflict",
                "ChallengeOrderConflict",
                "ChallengeTemplateConflict",
                "RevisionConflict",
                "LifecycleStateConflict",
                "ChallengeTemplateNotFound",
                "ChallengeTemplateModeMismatch"
            ]);
    }

    [Test]
    public async Task Update_body_requires_complete_revision_fenced_payload()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var request = ResolveSchema(
            root,
            Operation(root, ItemPath, "put")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(PropertyNames(request))
            .IsEquivalentTo(["customTitle", "baseScore", "order", "isPublished", "expectedRevision"]);
        await Assert.That(RequiredPropertyNames(request))
            .IsEquivalentTo(["customTitle", "baseScore", "order", "isPublished", "expectedRevision"]);
        AssertNonNegativeInt32(
            request.GetProperty("properties").GetProperty("expectedRevision"));
    }

    [Test]
    public async Task Delete_and_restore_require_non_negative_revision_query()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        foreach (var operation in new[]
        {
            Operation(root, ItemPath, "delete"),
            Operation(root, RestorePath, "post")
        })
        {
            var parameter = operation.GetProperty("parameters")
                .EnumerateArray()
                .Single(item =>
                    item.GetProperty("name").GetString() == "expectedRevision"
                    && item.GetProperty("in").GetString() == "query");

            await Assert.That(parameter.GetProperty("required").GetBoolean()).IsTrue();
            AssertNonNegativeInt32(parameter.GetProperty("schema"));
        }
    }

    private static JsonElement Operation(
        JsonElement root,
        string path,
        string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

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

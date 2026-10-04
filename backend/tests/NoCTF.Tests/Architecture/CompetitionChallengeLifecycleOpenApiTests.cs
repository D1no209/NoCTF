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
            Operation(root, ItemPath, "patch"),
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
            .IsEquivalentTo(["code", "detail"]);
        await Assert.That(codeSchema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(codeSchema.GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray())
            .IsEquivalentTo([
                "ResourceIdConflict",
                "ChallengeOrderConflict",
                "ChallengeTemplateConflict",
                "LifecycleStateConflict",
                "ChallengeTemplateNotFound",
                "ChallengeTemplateModeMismatch",
                "ExperimentalFeatureDisabled"
            ]);
    }

    [Test]
    public async Task Update_body_requires_complete_last_write_wins_payload()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var request = ResolveSchema(
            root,
            Operation(root, ItemPath, "patch")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(PropertyNames(request))
            .IsEquivalentTo(["presentation", "rules"]);
        var presentationProperty = request.GetProperty("properties").GetProperty("presentation");
        var presentation = ResolveSchema(root,
            presentationProperty.GetProperty("oneOf").EnumerateArray().First());
        await Assert.That(PropertyNames(presentation))
            .IsEquivalentTo(["customTitle", "order", "isPublished", "directionId", "tags"]);
    }

    [Test]
    public async Task Delete_and_restore_only_bind_resource_path_parameters()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        foreach (var operation in new[]
        {
            Operation(root, ItemPath, "delete"),
            Operation(root, RestorePath, "post")
        })
        {
            await Assert.That(operation.GetProperty("parameters")
                .EnumerateArray()
                .All(item => item.GetProperty("in").GetString() == "path"))
                .IsTrue();
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

    private static async Task<JsonDocument> ReadSwaggerAsync()
    {
        var backend = FindBackendRoot();
        return JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend, "artifacts", "openapi", "v1.json")));
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

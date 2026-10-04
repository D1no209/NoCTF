using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class GitOpsApiOpenApiContractTests
{
    [Test]
    public async Task GitOps_routes_expose_the_current_methods_without_legacy_configuration()
    {
        using var swagger = await ReadSwaggerAsync();
        var paths = swagger.RootElement.GetProperty("paths");
        var expected = new Dictionary<string, string[]>
        {
            ["/api/v1/auth/me"] = ["get"],
            ["/api/v1/admin/competitions/{competitionId}"] = ["get"],
            ["/api/v1/admin/competitions/{competitionId}/challenges"] = ["get", "post"],
            ["/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}"] =
                ["delete", "get", "patch"],
            ["/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore"] =
                ["post"],
            ["/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints"] =
                ["get", "post"],
            ["/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}"] =
                ["delete", "get", "put"],
            ["/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/restore"] =
                ["post"],
            ["/api/v1/admin/challenges"] = ["post"],
            ["/api/v1/admin/challenges/{challengeId}"] = ["delete", "get", "patch"],
            ["/api/v1/admin/challenges/{challengeId}/restore"] = ["post"],
            ["/api/v1/admin/challenges/{challengeId}/attachments"] = ["get", "post"],
            ["/api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}"] = ["delete"],
            ["/api/v1/admin/challenges/{challengeId}/attachments/{attachmentId}/restore"] = ["post"],
            ["/api/v1/admin/challenges/{challengeId}/flags"] = ["get", "post"],
            ["/api/v1/admin/challenges/{challengeId}/flags/{flagId}"] = ["delete", "get", "put"],
            ["/api/v1/admin/challenges/{challengeId}/flags/{flagId}/restore"] = ["post"],
            ["/api/v1/admin/platform/bots"] = ["post"],
            ["/api/v1/admin/platform/users/{userId}/tokens"] = ["delete", "post"]
        };

        foreach (var (path, methods) in expected)
        {
            await Assert.That(paths.TryGetProperty(path, out var route)).IsTrue();
            foreach (var method in methods)
                await Assert.That(route.TryGetProperty(method, out _)).IsTrue();
        }

        await Assert.That(paths.TryGetProperty(
            "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration",
            out _)).IsFalse();
    }

    [Test]
    public async Task GitOps_reads_use_aggregate_response_shapes()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        var competition = ResponseSchema(root,
            "/api/v1/admin/competitions/{competitionId}", "get", "200");
        await Assert.That(PropertyNames(competition)).Contains("competition");
        await Assert.That(PropertyNames(competition)).Contains("capabilities");

        var challenge = ResponseSchema(root,
            "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            "get", "200");
        await Assert.That(PropertyNames(challenge))
            .IsEquivalentTo(["challenge", "mode", "competitionStatus", "rules"]);
    }

    [Test]
    public async Task Attachment_upload_exposes_optional_stable_ids_with_batch_files()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var operation = root.GetProperty("paths")
            .GetProperty("/api/v1/admin/challenges/{challengeId}/attachments")
            .GetProperty("post");
        var request = ResolveSchema(root, operation.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema"));

        await Assert.That(PropertyNames(request)).IsEquivalentTo([
            "deliveryPolicy", "downloadFileName", "attachmentIds", "files"
        ]);
        await Assert.That(RequiredPropertyNames(request))
            .IsEquivalentTo(["deliveryPolicy", "files"]);
        var attachmentIds = request.GetProperty("properties").GetProperty("attachmentIds");
        await Assert.That(ResolveNullableSchema(root, attachmentIds).GetProperty("type").GetString()).IsEqualTo("array");
        await Assert.That(attachmentIds.GetProperty("nullable").GetBoolean()).IsTrue();
        await Assert.That(ResolveNullableSchema(root, attachmentIds).GetProperty("items").GetProperty("format").GetString())
            .IsEqualTo("uuid");
    }

    [Test]
    public async Task GitOps_writes_use_typed_definition_and_rules_contracts()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;

        var createTemplate = RequestSchema(root, "/api/v1/admin/challenges", "post");
        await Assert.That(PropertyNames(createTemplate)).Contains("definition");
        await Assert.That(PropertyNames(createTemplate)).DoesNotContain("definitionJson");
        var definition = ResolveSchema(root,
            createTemplate.GetProperty("properties").GetProperty("definition"));
        await Assert.That(RequiredPropertyNames(definition)).Contains("mode");
        await Assert.That(PropertyNames(definition)).Contains("ctf");
        await Assert.That(PropertyNames(definition)).Contains("awd");
        await Assert.That(PropertyNames(definition)).Contains("awdp");
        await Assert.That(PropertyNames(definition)).Contains("koh");

        var patchTemplate = RequestSchema(
            root, "/api/v1/admin/challenges/{challengeId}", "patch");
        var content = ResolveSchema(root,
            patchTemplate.GetProperty("properties").GetProperty("content"));
        await Assert.That(PropertyNames(content)).Contains("definition");
        await Assert.That(PropertyNames(content)).DoesNotContain("definitionJson");

        var challengeDetail = ResponseSchema(root,
            "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            "get", "200");
        await Assert.That(PropertyNames(challengeDetail)).Contains("rules");
        await Assert.That(PropertyNames(challengeDetail)).DoesNotContain("rulesJson");

        var patchChallenge = RequestSchema(root,
            "/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}",
            "patch");
        var rulesPatch = ResolveSchema(root,
            patchChallenge.GetProperty("properties").GetProperty("rules"));
        await Assert.That(PropertyNames(rulesPatch)).IsEquivalentTo(["configuration"]);
        await Assert.That(PropertyNames(rulesPatch)).DoesNotContain("json");
        var rules = ResolveSchema(root,
            rulesPatch.GetProperty("properties").GetProperty("configuration"));
        await Assert.That(RequiredPropertyNames(rules)).Contains("mode");
        await Assert.That(PropertyNames(rules)).Contains("ctf");
        await Assert.That(PropertyNames(rules)).Contains("awd");
        await Assert.That(PropertyNames(rules)).Contains("awdp");
        await Assert.That(PropertyNames(rules)).Contains("koh");
    }

    private static JsonElement RequestSchema(JsonElement root, string path, string method)
    {
        var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        return ResolveSchema(root, schema);
    }

    private static JsonElement ResponseSchema(
        JsonElement root,
        string path,
        string method,
        string status)
    {
        var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema");
        return ResolveSchema(root, schema);
    }

    private static JsonElement ResolveNullableSchema(JsonElement root, JsonElement schema)
        => ResolveSchema(root, schema.TryGetProperty("oneOf", out var choices) ? choices[0] : schema);

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        while (true)
        {
            if (schema.TryGetProperty("$ref", out var reference))
            {
                schema = root.GetProperty("components").GetProperty("schemas")
                    .GetProperty(reference.GetString()!.Split('/')[^1]);
                continue;
            }
            if (schema.TryGetProperty("oneOf", out var oneOf)
                && oneOf.GetArrayLength() == 1)
            {
                schema = oneOf[0];
                continue;
            }
            break;
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

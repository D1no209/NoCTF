using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class SubmitFlagOpenApiTests
{
    private const string SubmissionPath =
        "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/flag-submissions";

    [Test]
    public async Task Request_documents_single_and_batch_alternatives_without_requiring_either()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var requestBody = root.GetProperty("paths")
            .GetProperty(SubmissionPath)
            .GetProperty("post")
            .GetProperty("requestBody");
        var request = ResolveSchema(
            root,
            requestBody
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(requestBody.GetProperty("required").GetBoolean()).IsTrue();
        await Assert.That(PropertyNames(request))
            .IsEquivalentTo(["flag", "flags"]);
        await Assert.That(RequiredPropertyNames(request))
            .IsEquivalentTo(Array.Empty<string>());

        var properties = request.GetProperty("properties");
        var flag = properties.GetProperty("flag");
        await Assert.That(flag.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(flag.GetProperty("nullable").GetBoolean()).IsTrue();

        var flags = properties.GetProperty("flags");
        await Assert.That(flags.GetProperty("type").GetString()).IsEqualTo("array");
        await Assert.That(flags.GetProperty("nullable").GetBoolean()).IsTrue();
        await Assert.That(flags.GetProperty("items").GetProperty("type").GetString())
            .IsEqualTo("string");
    }

    [Test]
    public async Task Practice_reuses_the_standard_contract_without_a_team_marker()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        await Assert.That(root.GetProperty("paths").TryGetProperty(
                "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/practice-flag",
                out _))
            .IsFalse();
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var admissionCodes = schemas
            .GetProperty("NoCTFAPIEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        await Assert.That(admissionCodes).Contains("RuntimeNotRunning");
        var teamProperties = schemas
            .GetProperty("NoCTFAPIEndpointsTeamsTeamResponse")
            .GetProperty("properties");
        await Assert.That(teamProperties.TryGetProperty("isPracticeTeam", out _)).IsFalse();
    }

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

    private static string[] RequiredPropertyNames(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray()
                .Select(property => property.GetString()!)
                .ToArray()
            : [];

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

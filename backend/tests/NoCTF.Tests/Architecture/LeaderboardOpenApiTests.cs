using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class LeaderboardOpenApiTests
{
    private const string LeaderboardPath =
        "/api/v1/competitions/{competitionId}/leaderboard";
    private const string AdminVisibilityPath =
        "/api/v1/admin/competitions/{competitionId}/leaderboard-visibility";
    private const string ChallengeListPath =
        "/api/v1/competitions/{competitionId}/challenges";

    [Test]
    public async Task Response_exposes_ranked_bloods_and_subject_slot_blood_details()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var response = ResolveSchema(
            root,
            root.GetProperty("paths")
                .GetProperty(LeaderboardPath)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var responseProperties = response.GetProperty("properties");

        await Assert.That(responseProperties.TryGetProperty("bloods", out var bloods))
            .IsTrue();
        await Assert.That(responseProperties.TryGetProperty("firstBloods", out _))
            .IsFalse();

        var blood = ResolveSchema(root, bloods.GetProperty("items"));
        await Assert.That(PropertyNames(blood)).IsEquivalentTo([
            "slotKey",
            "slotKind",
            "bloodRank",
            "teamId",
            "teamName",
            "occurredAt"
        ]);
        var rank = ResolveSchema(
            root,
            blood.GetProperty("properties").GetProperty("bloodRank"));
        await Assert.That(rank.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(rank.GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()!))
            .IsEquivalentTo(["First", "Second", "Third"]);
        await Assert.That(rank.GetProperty("x-enumNames").EnumerateArray()
                .Select(value => value.GetString()!))
            .IsEquivalentTo(["First", "Second", "Third"]);

        var subject = ResolveSchema(
            root,
            responseProperties.GetProperty("subjects").GetProperty("items"));
        var slot = ResolveSchema(
            root,
            subject.GetProperty("properties")
                .GetProperty("slots")
                .GetProperty("items"));
        var slotProperties = slot.GetProperty("properties");
        await Assert.That(slotProperties.TryGetProperty("bloodRank", out var slotRank))
            .IsTrue();
        await Assert.That(slotRank.GetProperty("nullable").GetBoolean()).IsTrue();
        await Assert.That(slotProperties.TryGetProperty("bloodAt", out var bloodAt))
            .IsTrue();
        await Assert.That(bloodAt.GetProperty("nullable").GetBoolean()).IsTrue();
        await Assert.That(slotProperties.TryGetProperty("firstBloodAt", out _))
            .IsFalse();
    }

    [Test]
    public async Task Contract_exposes_typed_visibility_scopes_and_admin_operations()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var leaderboard = ResolveSchema(
            root,
            root.GetProperty("paths")
                .GetProperty(LeaderboardPath)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var properties = leaderboard.GetProperty("properties");
        await Assert.That(properties.TryGetProperty("visibility", out var visibility))
            .IsTrue();
        await Assert.That(properties.TryGetProperty("dataScope", out var dataScope))
            .IsTrue();
        await Assert.That(properties.TryGetProperty("dataAsOf", out _))
            .IsTrue();
        await Assert.That(EnumNames(ResolveSchema(root, visibility)))
            .IsEquivalentTo(["Normal", "Frozen", "Blackout"]);
        await Assert.That(EnumNames(ResolveSchema(root, dataScope)))
            .IsEquivalentTo(["Live", "Frozen", "Hidden"]);

        var adminPath = root.GetProperty("paths").GetProperty(AdminVisibilityPath);
        await Assert.That(adminPath.GetProperty("get").GetProperty("operationId").GetString())
            .IsEqualTo("AdminGetCompetitionLeaderboardVisibility");
        await Assert.That(adminPath.GetProperty("put").GetProperty("operationId").GetString())
            .IsEqualTo("AdminUpdateCompetitionLeaderboardVisibility");
        var request = ResolveSchema(
            root,
            adminPath.GetProperty("put")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        await Assert.That(PropertyNames(request)).IsEquivalentTo([
            "visibility",
            "startsAt",
            "expectedRevision",
            "reason"
        ]);
        await Assert.That(request.GetProperty("required").EnumerateArray()
                .Select(property => property.GetString()!))
            .IsEquivalentTo(["visibility", "expectedRevision"]);
        await Assert.That(adminPath.GetProperty("put").GetProperty("responses")
                .EnumerateObject().Select(response => response.Name))
            .IsEquivalentTo(["200", "400", "409", "404", "401", "403"]);
    }

    [Test]
    public async Task Challenge_contract_hides_score_without_hiding_playable_content()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var list = ResolveSchema(
            root,
            root.GetProperty("paths")
                .GetProperty(ChallengeListPath)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var listProperties = list.GetProperty("properties");
        await Assert.That(listProperties.TryGetProperty("leaderboardVisibility", out _))
            .IsTrue();
        await Assert.That(listProperties.TryGetProperty("dataScope", out _))
            .IsTrue();
        var challenge = ResolveSchema(
            root,
            listProperties.GetProperty("items").GetProperty("items"));
        var challengeProperties = challenge.GetProperty("properties");
        await Assert.That(challengeProperties.GetProperty("baseScore")
                .GetProperty("nullable").GetBoolean())
            .IsTrue();
        await Assert.That(challengeProperties.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(challengeProperties.TryGetProperty("description", out _)).IsTrue();
        await Assert.That(challengeProperties.TryGetProperty("urls", out _)).IsTrue();
    }

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

    private static string[] EnumNames(JsonElement schema) =>
        schema.GetProperty("x-enumNames")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

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

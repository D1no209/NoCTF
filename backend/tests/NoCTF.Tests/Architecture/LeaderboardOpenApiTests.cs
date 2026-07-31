using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class LeaderboardOpenApiTests
{
    private const string LeaderboardPath =
        "/api/v1/competitions/{competitionId}/leaderboard";

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
        await Assert.That(rank.GetProperty("enum").EnumerateArray()
                .Select(value => value.GetInt32()))
            .IsEquivalentTo([1, 2, 3]);
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

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
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

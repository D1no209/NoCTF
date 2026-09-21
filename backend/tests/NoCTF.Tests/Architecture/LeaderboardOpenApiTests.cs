using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class LeaderboardOpenApiTests
{
    private const string LeaderboardPath =
        "/api/v1/competitions/{competitionId}/leaderboard";
    private const string AdminVisibilityPath =
        "/api/v1/admin/competitions/{competitionId}";
    private const string ChallengeListPath =
        "/api/v1/competitions/{competitionId}/challenges";
    private const string ScoreboardSlotDetailPath =
        "/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/columns/{columnIndex}";
    private const string ScoreboardAdjustmentDetailPath =
        "/api/v1/competitions/{competitionId}/leaderboard/teams/{teamId}/adjustments";

    [Test]
    public async Task Response_exposes_a_sparse_team_by_challenge_matrix()
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

        await Assert.That(PropertyNames(response)).IsEquivalentTo([
            "competitionId",
            "version",
            "schemaRevision",
            "generatedAt",
            "currentRoundId",
            "actors",
            "teams",
            "tracks",
            "tracksEnabled",
            "currentChallengeScores",
            "visibility",
            "dataScope",
            "dataAsOf"
        ]);
        await Assert.That(ResolveSchema(root, responseProperties.GetProperty("version"))
                .GetProperty("type").GetString())
            .IsEqualTo("string");
        await Assert.That(ResolveSchema(root, responseProperties.GetProperty("schemaRevision"))
                .GetProperty("type").GetString())
            .IsEqualTo("string");
        var team = ResolveSchema(
            root,
            responseProperties.GetProperty("teams").GetProperty("items"));
        await Assert.That(PropertyNames(team)).IsEquivalentTo([
            "teamId",
            "teamName",
            "trackKey",
            "rank",
            "rankingState",
            "totalScore",
            "scoreOutsideWindow",
            "attackScore",
            "defenseScore",
            "challengeScores",
            "memberContributions",
            "achievements",
            "globalAdjustments",
            "globalAdjustmentCount",
            "slots"
        ]);
        var challengeScore = ResolveSchema(
            root,
            team.GetProperty("properties").GetProperty("challengeScores").GetProperty("items"));
        await Assert.That(PropertyNames(challengeScore)).IsEquivalentTo([
            "competitionChallengeId",
            "attackScore",
            "defenseScore"
        ]);
        var memberContribution = ResolveSchema(
            root,
            team.GetProperty("properties").GetProperty("memberContributions").GetProperty("items"));
        await Assert.That(PropertyNames(memberContribution)).IsEquivalentTo([
            "userId",
            "displayName",
            "earnedPoints"
        ]);
        var slot = ResolveSchema(
            root,
            team.GetProperty("properties").GetProperty("slots").GetProperty("items"));
        await Assert.That(PropertyNames(slot)).IsEquivalentTo([
            "columnIndex",
            "scoreState",
            "earnedPoints",
            "deductedPoints",
            "netPoints",
            "entryCount",
            "breakdown",
            "entries",
            "offenseState",
            "defenseState"
        ]);
        var scoreState = ResolveSchema(
            root,
            slot.GetProperty("properties").GetProperty("scoreState"));
        await Assert.That(scoreState.GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()!))
            .IsEquivalentTo(["Pending", "Provisional", "Settled"]);
        await Assert.That(scoreState.GetProperty("x-enumNames").EnumerateArray()
                .Select(value => value.GetString()!))
            .IsEquivalentTo(["Pending", "Provisional", "Settled"]);
        foreach (var propertyName in new[] { "offenseState", "defenseState" })
        {
            var operationState = ResolveSchema(
                root,
                slot.GetProperty("properties").GetProperty(propertyName));
            await Assert.That(operationState.GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString()!))
                .IsEquivalentTo(["None", "Failed", "Succeeded"]);
            await Assert.That(operationState.GetProperty("x-enumNames").EnumerateArray()
                    .Select(value => value.GetString()!))
                .IsEquivalentTo(["None", "Failed", "Succeeded"]);
        }
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
            .IsEqualTo("AdminGetCompetition");
        await Assert.That(adminPath.GetProperty("patch").GetProperty("operationId").GetString())
            .IsEqualTo("AdminPatchCompetition");
        var request = ResolveSchema(
            root,
            adminPath.GetProperty("patch")
                .GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var visibilityRequest = ResolveSchema(root,
            request.GetProperty("properties").GetProperty("leaderboardVisibility")
                .GetProperty("oneOf").EnumerateArray().First());
        await Assert.That(PropertyNames(visibilityRequest)).IsEquivalentTo([
            "frozenStartAt",
            "hiddenStartAt",
            "reason"
        ]);
        await Assert.That(adminPath.GetProperty("patch").GetProperty("responses")
                .TryGetProperty("200", out _)).IsTrue();
    }

    [Test]
    public async Task Slot_detail_exposes_a_page_local_actor_catalog()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var response = ResolveSchema(
            root,
            root.GetProperty("paths")
                .GetProperty(ScoreboardSlotDetailPath)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(PropertyNames(response)).IsEquivalentTo([
            "competitionId",
            "teamId",
            "columnIndex",
            "scoreState",
            "earnedPoints",
            "deductedPoints",
            "netPoints",
            "entryCount",
            "breakdown",
            "actors",
            "items",
            "nextCursor"
        ]);
    }

    [Test]
    public async Task Adjustment_detail_exposes_complete_cursor_paged_history()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var response = ResolveSchema(
            root,
            root.GetProperty("paths")
                .GetProperty(ScoreboardAdjustmentDetailPath)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));

        await Assert.That(PropertyNames(response)).IsEquivalentTo([
            "competitionId",
            "teamId",
            "entryCount",
            "actors",
            "items",
            "nextCursor"
        ]);
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
        await Assert.That(challengeProperties.TryGetProperty("baseScore", out _)).IsFalse();
        await Assert.That(challengeProperties.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(challengeProperties.TryGetProperty("description", out _)).IsTrue();
        await Assert.That(challengeProperties.TryGetProperty("accesses", out _)).IsTrue();
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

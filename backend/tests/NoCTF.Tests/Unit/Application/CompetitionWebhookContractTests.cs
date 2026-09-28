using System.Text.Json;

namespace NoCTF.Tests.Unit.Application;

public sealed class CompetitionWebhookContractTests
{
    private static readonly string[] EventTypes =
    [
        "com.noctf.competition.lifecycle.changed.v1",
        "com.noctf.competition.challenge.published.v1",
        "com.noctf.competition.challenge.updated.v1",
        "com.noctf.competition.hint.published.v1",
        "com.noctf.competition.announcement.published.v1",
        "com.noctf.competition.team.banned.v1",
        "com.noctf.competition.team.ban.corrected.v1",
        "com.noctf.competition.blood.awarded.v1",
        "com.noctf.competition.awdp.break.resolved.v1",
        "com.noctf.competition.awdp.fix.resolved.v1",
        "com.noctf.webhook.test.v1"
    ];

    [Test]
    public async Task Schema_freezes_the_v1_event_catalog_and_required_envelope()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "artifacts",
            "webhooks",
            "competition-events-v1.schema.json");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = schema.RootElement;

        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        var types = root.GetProperty("properties")
            .GetProperty("type")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        await Assert.That(required).IsEquivalentTo([
            "specversion", "id", "source", "type", "subject", "time",
            "datacontenttype", "dataschema", "data"
        ]);
        await Assert.That(types).IsEquivalentTo(EventTypes);
        await Assert.That(root.GetProperty("properties")
            .GetProperty("specversion").GetProperty("const").GetString())
            .IsEqualTo("1.0");
        await Assert.That(root.GetProperty("properties")
            .GetProperty("datacontenttype").GetProperty("const").GetString())
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task Blood_variant_requires_identity_award_and_non_null_resources()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "backend", "artifacts", "webhooks",
            "competition-events-v1.schema.json");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var variant = schema.RootElement.GetProperty("allOf")[0].GetProperty("then")
            .GetProperty("properties").GetProperty("data").GetProperty("properties");
        var dataRequired = schema.RootElement.GetProperty("allOf")[0]
            .GetProperty("then").GetProperty("properties").GetProperty("data")
            .GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        var eventRequired = variant.GetProperty("event").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var resourceRequired = variant.GetProperty("resources").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();

        await Assert.That(eventRequired).IsEquivalentTo([
            "competitionId", "teamId", "competitionChallengeId", "award"
        ]);
        await Assert.That(resourceRequired).IsEquivalentTo([
            "competition", "challenge", "leaderboard"
        ]);
        await Assert.That(dataRequired).IsEquivalentTo([
            "capturedAt", "eventSequence", "requiredProjectionVersion",
            "publicProjectionVersion",
            "competitionRevision", "event", "resources"
        ]);
        await Assert.That(variant.GetProperty("resources").GetProperty("properties")
            .GetProperty("leaderboard").GetProperty("properties")
            .GetProperty("dataScope").GetProperty("enum").GetArrayLength())
            .IsEqualTo(2);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "backend", "NoCTF.slnx")))
            {
                return File.Exists(Path.Combine(directory.FullName, "backend", "NoCTF.slnx"))
                    ? directory.FullName
                    : directory.Parent!.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}

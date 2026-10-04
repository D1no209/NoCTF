using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class TeamWriteUpOpenApiTests
{
    [Test]
    public async Task WriteUp_upload_and_download_contracts_are_pdf_specific_and_bearer_protected()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var upload = Operation(
            root,
            "/api/v1/competitions/{competitionId}/teams/me/writeup",
            "put");
        await Assert.That(upload.GetProperty("requestBody")
                .GetProperty("content")
                .TryGetProperty("multipart/form-data", out _))
            .IsTrue();
        await Assert.That(upload.GetProperty("responses").TryGetProperty("413", out _))
            .IsTrue();
        await Assert.That(upload.GetProperty("responses").TryGetProperty("409", out _))
            .IsTrue();
        await Assert.That(upload.GetProperty("responses").TryGetProperty("422", out _))
            .IsTrue();

        foreach (var path in new[]
        {
            "/api/v1/competitions/{competitionId}/teams/me/writeup/content",
            "/api/v1/competitions/{competitionId}/teams/{teamId}/writeup/content"
        })
        {
            var download = Operation(root, path, "get");
            await Assert.That(download.GetProperty("responses")
                    .GetProperty("200")
                    .GetProperty("content")
                    .TryGetProperty("application/pdf", out _))
                .IsTrue();
            await Assert.That(HasBearer(download)).IsTrue();
        }
    }

    [Test]
    public async Task Competition_contract_exposes_the_required_WriteUp_submission_window()
    {
        using var swagger = await ReadSwaggerAsync();
        var schemas = swagger.RootElement.GetProperty("components").GetProperty("schemas");
        var competition = schemas.GetProperty(
            "NoCTFAPIEndpointsCompetitionsCompetitionResponse");
        foreach (var property in new[]
                 {
                     "writeUpSubmissionRequired",
                     "writeUpSubmissionDeadlineHours",
                     "writeUpSubmissionDeadlineAt"
                 })
        {
            await Assert.That(competition.GetProperty("properties")
                    .TryGetProperty(property, out _))
                .IsTrue();
        }

        var metadata = schemas.GetProperty(
            "NoCTFAPIEndpointsAdministrationCompetitionsCompetitionMetadataPatchRequest");
        var required = metadata.GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();
        await Assert.That(required).Contains("writeUpSubmissionRequired");
        await Assert.That(required).Contains("writeUpSubmissionDeadlineHours");
    }

    [Test]
    public async Task Staff_review_exposes_scores_and_a_typed_consultation_contract()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var review = Operation(
            root,
            "/api/v1/competitions/{competitionId}/writeups",
            "get");
        var reviewSchema = ResolveSchema(
            root,
            review.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        await Assert.That(reviewSchema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name))
            .IsEquivalentTo(["scoreboardAvailable", "canJudge", "items"]);
        var reviewItem = ResolveSchema(
            root,
            ResolveSchema(root, reviewSchema.GetProperty("properties")
                .GetProperty("items")).GetProperty("items"));
        await Assert.That(reviewItem.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name))
            .IsEquivalentTo([
                "writeUp",
                "originalTotalScore",
                "originalRank",
                "adjustedTotalScore",
                "adjustedRank",
                "challengeScores"
            ]);

        var consultation = Operation(
            root,
            "/api/v1/competitions/{competitionId}/teams/{teamId}/writeup/consultations",
            "post");
        await Assert.That(consultation.GetProperty("responses").TryGetProperty("201", out _))
            .IsTrue();
        await Assert.That(consultation.GetProperty("responses").TryGetProperty("403", out _))
            .IsTrue();
        await Assert.That(HasBearer(consultation)).IsTrue();
    }

    [Test]
    public async Task Staff_preview_issues_a_protected_grant_for_an_inline_pdf_stream()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var issue = Operation(
            root,
            "/api/v1/competitions/{competitionId}/teams/{teamId}/writeup/preview",
            "post");
        var preview = Operation(
            root,
            "/api/v1/writeup-previews/{competitionId}/{teamId}",
            "get");

        await Assert.That(HasBearer(issue)).IsTrue();
        await Assert.That(issue.GetProperty("responses").TryGetProperty("200", out _))
            .IsTrue();
        await Assert.That(preview.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .TryGetProperty("application/pdf", out _))
            .IsTrue();
        await Assert.That(preview.GetProperty("responses").TryGetProperty("401", out _))
            .IsTrue();
        await Assert.That(HasBearer(preview)).IsFalse();
    }

    private static JsonElement Operation(JsonElement root, string path, string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static bool HasBearer(JsonElement operation) =>
        operation.TryGetProperty("security", out var security)
        && security
            .EnumerateArray()
            .Any(requirement => requirement.TryGetProperty("Bearer", out _));

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var reference))
        {
            schema = root.GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(reference.GetString()!.Split('/')[^1]);
        }
        return schema;
    }

    private static async Task<JsonDocument> ReadSwaggerAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
            throw new DirectoryNotFoundException("Backend root was not found.");
        return JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory.FullName, "artifacts", "openapi", "v1.json")));
    }
}

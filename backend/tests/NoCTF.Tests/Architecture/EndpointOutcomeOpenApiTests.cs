using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class EndpointOutcomeOpenApiTests
{
    private const string ChallengePrefix =
        "/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}";

    [Test]
    public async Task Submission_and_runtime_outcome_matrices_match_execution_contracts()
    {
        using var swagger = await ReadSwaggerAsync();
        var root = swagger.RootElement;
        var contracts = new[]
        {
            new OutcomeContract(
                $"{ChallengePrefix}/flag-submissions",
                new Dictionary<string, ResponseBodyKind>
                {
                    ["202"] = ResponseBodyKind.Json,
                    ["400"] = ResponseBodyKind.Problem,
                    ["401"] = ResponseBodyKind.Empty,
                    ["403"] = ResponseBodyKind.Problem,
                    ["409"] = ResponseBodyKind.Problem,
                    ["429"] = ResponseBodyKind.Empty
                }),
            new OutcomeContract(
                $"{ChallengePrefix}/fix-submissions",
                new Dictionary<string, ResponseBodyKind>
                {
                    ["202"] = ResponseBodyKind.Json,
                    ["400"] = ResponseBodyKind.Problem,
                    ["401"] = ResponseBodyKind.Empty,
                    ["403"] = ResponseBodyKind.Problem,
                    ["409"] = ResponseBodyKind.Problem,
                    ["429"] = ResponseBodyKind.Empty
                }),
            RuntimeContract("start", includesValidationProblem: false),
            RuntimeContract("reset", includesValidationProblem: false),
            RuntimeContract("stop", includesValidationProblem: false),
            RuntimeContract("extend", includesValidationProblem: true)
        };

        foreach (var contract in contracts)
        {
            var responses = root.GetProperty("paths")
                .GetProperty(contract.Path)
                .GetProperty("post")
                .GetProperty("responses");
            await Assert.That(responses.EnumerateObject()
                    .Select(response => response.Name)
                    .ToArray())
                .IsEquivalentTo(contract.Responses.Keys.ToArray());

            foreach (var expected in contract.Responses)
                await AssertResponseBodyAsync(
                    responses.GetProperty(expected.Key),
                    expected.Value);
        }
    }

    private static OutcomeContract RuntimeContract(
        string action,
        bool includesValidationProblem)
    {
        var responses = new Dictionary<string, ResponseBodyKind>
        {
            ["202"] = ResponseBodyKind.Json,
            ["401"] = ResponseBodyKind.Empty,
            ["403"] = ResponseBodyKind.Empty,
            ["404"] = ResponseBodyKind.Empty,
            ["409"] = ResponseBodyKind.Empty,
            ["503"] = ResponseBodyKind.Problem
        };
        if (includesValidationProblem)
            responses["400"] = ResponseBodyKind.Problem;
        return new($"{ChallengePrefix}/runtime/{action}", responses);
    }

    private static async Task AssertResponseBodyAsync(
        JsonElement response,
        ResponseBodyKind bodyKind)
    {
        var hasContent = response.TryGetProperty("content", out var content)
            && content.EnumerateObject().Any();
        if (bodyKind == ResponseBodyKind.Empty)
        {
            await Assert.That(hasContent).IsFalse();
            return;
        }

        await Assert.That(hasContent).IsTrue();
        var mediaType = bodyKind == ResponseBodyKind.Json
            ? "application/json"
            : "application/problem+json";
        await Assert.That(content.TryGetProperty(mediaType, out var body))
            .IsTrue();
        await Assert.That(body.TryGetProperty("schema", out var schema))
            .IsTrue();
        if (bodyKind != ResponseBodyKind.Problem)
            return;

        await Assert.That(schema.TryGetProperty("$ref", out var reference))
            .IsTrue();
        await Assert.That(reference.GetString()!.EndsWith(
                "ProblemDetails",
                StringComparison.Ordinal))
            .IsTrue();
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

    private sealed record OutcomeContract(
        string Path,
        IReadOnlyDictionary<string, ResponseBodyKind> Responses);

    private enum ResponseBodyKind
    {
        Empty,
        Json,
        Problem
    }
}

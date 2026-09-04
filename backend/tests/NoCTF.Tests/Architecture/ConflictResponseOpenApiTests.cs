using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class ConflictResponseOpenApiTests
{
    [Test]
    public async Task Every_conflict_response_explains_the_reason()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(OpenApiPath()));
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var unexplained = new List<string>();

        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.NameEquals("parameters")
                    || !method.Value.GetProperty("responses").TryGetProperty("409", out var conflict))
                {
                    continue;
                }

                if (!conflict.TryGetProperty("content", out var content)
                    || !TryGetResponseSchema(content, out var responseSchema)
                    || !TryResolveSchema(responseSchema, schemas, out var schema)
                    || !schema.TryGetProperty("properties", out var properties)
                    || !HasExplanation(properties))
                {
                    unexplained.Add(
                        $"{method.Name.ToUpperInvariant()} {path.Name}"
                        + $" ({method.Value.GetProperty("operationId").GetString()})");
                }
            }
        }

        await Assert.That(unexplained).IsEmpty();
    }

    [Test]
    public async Task Endpoint_source_does_not_return_a_bodyless_conflict()
    {
        var endpointRoot = Path.Combine(
            RepositoryRoot(),
            "backend",
            "src",
            "NoCTF.API",
            "Endpoints");
        var offenders = Directory.EnumerateFiles(endpointRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "TypedResults.Conflict()",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path))
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    private static bool TryGetResponseSchema(
        JsonElement content,
        out JsonElement schema)
    {
        foreach (var mediaType in content.EnumerateObject())
        {
            if (mediaType.Value.TryGetProperty("schema", out schema))
                return true;
        }

        schema = default;
        return false;
    }

    private static bool TryResolveSchema(
        JsonElement responseSchema,
        JsonElement schemas,
        out JsonElement schema)
    {
        if (!responseSchema.TryGetProperty("$ref", out var reference))
        {
            schema = responseSchema;
            return true;
        }

        var name = reference.GetString()?.Split('/').LastOrDefault();
        if (name is not null && schemas.TryGetProperty(name, out schema))
            return true;

        schema = default;
        return false;
    }

    private static bool HasExplanation(JsonElement properties) =>
        properties.TryGetProperty("detail", out _)
        || properties.TryGetProperty("message", out _)
        || properties.TryGetProperty("errors", out _);

    private static string OpenApiPath() => Path.Combine(
        RepositoryRoot(),
        "backend",
        "src",
        "NoCTF.API",
        "wwwroot",
        "openapi",
        "v1.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

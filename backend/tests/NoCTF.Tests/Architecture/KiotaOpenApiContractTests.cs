using System.Text.Json;
using System.Text.RegularExpressions;

namespace NoCTF.Tests.Architecture;

public sealed class KiotaOpenApiContractTests
{
    [Test]
    public async Task Both_validation_problem_variants_describe_localized_field_messages()
    {
        using var document = await ReadDocument();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(schema => schema.Name.EndsWith("ValidationProblemDetails", StringComparison.Ordinal)).ToArray();
        await Assert.That(schemas.Length).IsEqualTo(2);
        foreach (var schema in schemas)
        {
            var properties = schema.Value.GetProperty("properties");
            await Assert.That(properties.TryGetProperty("errors", out _)).IsTrue();
            await Assert.That(properties.TryGetProperty("errorMessages", out _)).IsTrue();
            await Assert.That(properties.TryGetProperty("messageKey", out _)).IsTrue();
            await Assert.That(properties.TryGetProperty("messageArguments", out _)).IsTrue();
        }
    }

    [Test]
    public async Task Every_local_schema_reference_resolves()
    {
        using var document = await ReadDocument();
        var references = Descendants(document.RootElement)
            .Where(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$ref", out _))
            .Select(value => value.GetProperty("$ref").GetString()!)
            .ToArray();
        await Assert.That(references.Length).IsGreaterThan(0);
        foreach (var reference in references)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected external OpenAPI reference: {reference}");
            var target = document.RootElement;
            foreach (var segment in reference[2..].Split('/'))
                if (!target.TryGetProperty(segment.Replace("~1", "/").Replace("~0", "~"), out target))
                    throw new InvalidOperationException($"Unresolved OpenAPI reference: {reference}");
        }
    }

    [Test]
    public async Task Human_verification_is_an_optional_header_and_never_a_JSON_field()
    {
        using var document = await ReadDocument();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var endpointFiles = Directory.EnumerateFiles(Path.Combine(BackendRoot(), "src", "NoCTF.API", "Endpoints"), "*.cs", SearchOption.AllDirectories);
        var checkedEndpoints = 0;
        foreach (var file in endpointFiles)
        {
            var source = await File.ReadAllTextAsync(file);
            if (!source.Contains("new HumanVerificationMetadata(", StringComparison.Ordinal)
                && !source.Contains("new NoCTF.API.Security.HumanVerificationMetadata(", StringComparison.Ordinal)) continue;
            var route = Regex.Match(source, "(Get|Post|Put|Patch|Delete)\\(\"([^\"]+)\"\\)");
            if (!route.Success) throw new InvalidOperationException($"No route found for {file}.");
            var operation = paths.GetProperty("/api/v1" + route.Groups[2].Value)
                .GetProperty(route.Groups[1].Value.ToLowerInvariant());
            var header = operation.GetProperty("parameters").EnumerateArray()
                .Single(parameter => parameter.GetProperty("name").GetString() == "X-NoCTF-Human-Verification");
            await Assert.That(header.GetProperty("in").GetString()).IsEqualTo("header");
            await Assert.That(header.TryGetProperty("required", out var required) && required.GetBoolean()).IsFalse();
            foreach (var code in new[] { "403", "503" })
                await Assert.That(operation.GetProperty("responses").GetProperty(code)
                    .GetProperty("content").TryGetProperty("application/problem+json", out _)).IsTrue();
            if (operation.TryGetProperty("requestBody", out var body))
                foreach (var content in body.GetProperty("content").EnumerateObject())
                {
                    var schema = Resolve(root, content.Value.GetProperty("schema"));
                    await Assert.That(schema.GetProperty("properties").TryGetProperty("humanVerificationToken", out _)).IsFalse();
                }
            checkedEndpoints++;
        }
        await Assert.That(checkedEndpoints).IsEqualTo(11);
    }

    [Test]
    public async Task Upload_files_are_binary_and_bodyless_commands_have_no_request_body()
    {
        using var document = await ReadDocument();
        var root = document.RootElement;
        var uploads = 0;
        foreach (var path in root.GetProperty("paths").EnumerateObject())
            foreach (var method in path.Value.EnumerateObject())
                if (method.Value.TryGetProperty("requestBody", out var body)
                    && body.GetProperty("content").TryGetProperty("multipart/form-data", out var form))
                {
                    var properties = Resolve(root, form.GetProperty("schema")).GetProperty("properties");
                    var file = properties.TryGetProperty("file", out var single)
                        ? Resolve(root, single)
                        : properties.TryGetProperty("image", out var image)
                        ? Resolve(root, image)
                        : Resolve(root, Resolve(root, properties.GetProperty("files")).GetProperty("items"));
                    await Assert.That(file.GetProperty("type").GetString()).IsEqualTo("string");
                    await Assert.That(file.GetProperty("format").GetString()).IsEqualTo("binary");
                    uploads++;
                }
        await Assert.That(uploads).IsEqualTo(12);
        foreach (var (path, method) in new[]
        {
            ("/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/{runtimeInstanceId}", "delete"),
            ("/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-defense-targets", "post"),
            ("/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/patch-verification-targets", "post")
        })
            await Assert.That(root.GetProperty("paths").GetProperty(path).GetProperty(method).TryGetProperty("requestBody", out _)).IsFalse();
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement value)
    {
        yield return value;
        var children = value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Select(property => property.Value)
            : value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().AsEnumerable() : [];
        foreach (var child in children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            return Resolve(root, root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]));
        return schema.TryGetProperty("oneOf", out var choices) ? Resolve(root, choices[0]) : schema;
    }

    private static async Task<JsonDocument> ReadDocument()
        => JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(BackendRoot(), "artifacts", "openapi", "v1.json")));

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NoCTF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Backend root was not found.");
    }
}

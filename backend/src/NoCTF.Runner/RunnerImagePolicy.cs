using NoCTF.PluginBase;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace NoCTF.Runner;

internal static class RunnerImagePolicy
{
    public static string? FindDisallowedContainerImage(ContainerConfig config, IReadOnlyCollection<string> allowedRegistries)
    {
        if (allowedRegistries.Count == 0)
            return null;

        foreach (var image in ReadContainerImages(config))
        {
            if (!ImageAllowed(image, allowedRegistries))
                return image;
        }

        return null;
    }

    public static string? FindDisallowedComposeImage(string composeYaml, IReadOnlyCollection<string> allowedRegistries)
    {
        if (allowedRegistries.Count == 0)
            return null;

        foreach (var image in ReadComposeImages(composeYaml))
        {
            if (!ImageAllowed(image, allowedRegistries))
                return image;
        }

        return null;
    }

    private static IEnumerable<string> ReadContainerImages(ContainerConfig config)
    {
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.Image))
            images.Add(config.Image);

        var spec = OrchestrationSpecSerializer.Read(config.OrchestrationJson);
        if (!string.IsNullOrWhiteSpace(spec.Image))
            images.Add(spec.Image);

        if (images.Count == 0)
            throw new InvalidOperationException("Container image is required.");

        return images.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static bool ImageAllowed(string image, IReadOnlyCollection<string> allowedRegistries)
    {
        if (string.IsNullOrWhiteSpace(image))
            return false;

        if (allowedRegistries.Count == 0)
            return true;

        var imageRef = ImageReference.Parse(image);
        return allowedRegistries.Any(allowed => ImageReference.ParseAllowed(allowed).Allows(imageRef));
    }

    private static IEnumerable<string> ReadComposeImages(string composeYaml)
    {
        if (string.IsNullOrWhiteSpace(composeYaml))
            throw new InvalidOperationException("Compose YAML cannot be empty.");

        var stream = new YamlStream();
        using var reader = new StringReader(composeYaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Compose YAML must be a mapping.");

        var services = GetMapping(root, "services")
            ?? throw new InvalidOperationException("Compose YAML must define services.");

        foreach (var (_, valueNode) in services.Children)
        {
            if (valueNode is not YamlMappingNode service)
                throw new InvalidOperationException("Compose service definition must be a mapping.");

            var image = Scalar(GetValue(service, "image"));
            if (string.IsNullOrWhiteSpace(image))
                throw new InvalidOperationException("Compose service image is required.");

            yield return image;

            var orchestrationImage = ReadNoctfOrchestrationImage(GetValue(service, "x-noctf-orchestration"));
            if (!string.IsNullOrWhiteSpace(orchestrationImage))
                yield return orchestrationImage;
        }
    }

    private static string? ReadNoctfOrchestrationImage(YamlNode? node)
    {
        var json = Scalar(node);
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("x-noctf-orchestration must be a JSON object.");

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("image") ||
                    property.Name.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : throw new InvalidOperationException("x-noctf-orchestration.image must be a string.");
                }
            }

            return null;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("x-noctf-orchestration must be valid JSON.", ex);
        }
    }

    private sealed record ImageReference(string Registry, string Repository)
    {
        public static ImageReference Parse(string image)
        {
            var value = StripScheme(image.Trim()).Trim('/');
            var digestIndex = value.IndexOf('@', StringComparison.Ordinal);
            if (digestIndex >= 0)
                value = value[..digestIndex];

            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                return new ImageReference(string.Empty, string.Empty);

            var first = segments[0];
            var hasRegistry = segments.Length > 1 &&
                              (first.Contains('.', StringComparison.Ordinal) ||
                               first.Contains(':', StringComparison.Ordinal) ||
                               first.Equals("localhost", StringComparison.OrdinalIgnoreCase));
            var registry = hasRegistry ? first.ToLowerInvariant() : "docker.io";
            var repository = string.Join('/', hasRegistry ? segments.Skip(1) : segments);
            repository = StripTag(repository).ToLowerInvariant();
            return new ImageReference(registry, repository);
        }

        public static AllowedImageReference ParseAllowed(string allowed)
        {
            var value = StripScheme(allowed.Trim()).Trim('/');
            var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                return new AllowedImageReference(string.Empty, string.Empty);

            var first = segments[0];
            var hasRegistry = first.Contains('.', StringComparison.Ordinal) ||
                              first.Contains(':', StringComparison.Ordinal) ||
                              first.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            var registry = hasRegistry ? first.ToLowerInvariant() : "docker.io";
            var repository = string.Join('/', hasRegistry ? segments.Skip(1) : segments).ToLowerInvariant();
            repository = StripTag(repository);
            return new AllowedImageReference(registry, repository);
        }

        private static string StripScheme(string value)
        {
            var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
            return schemeIndex >= 0 ? value[(schemeIndex + 3)..] : value;
        }

        private static string StripTag(string repository)
        {
            var lastSlash = repository.LastIndexOf("/", StringComparison.Ordinal);
            var lastColon = repository.LastIndexOf(":", StringComparison.Ordinal);
            return lastColon > lastSlash ? repository[..lastColon] : repository;
        }
    }

    private sealed record AllowedImageReference(string Registry, string Repository)
    {
        public bool Allows(ImageReference image)
        {
            if (!Registry.Equals(image.Registry, StringComparison.OrdinalIgnoreCase))
                return false;

            return string.IsNullOrWhiteSpace(Repository) ||
                   image.Repository.Equals(Repository, StringComparison.OrdinalIgnoreCase) ||
                   image.Repository.StartsWith(Repository + "/", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key)
        => GetValue(mapping, key) as YamlMappingNode;

    private static YamlNode? GetValue(YamlMappingNode mapping, string key)
        => mapping.Children.FirstOrDefault(kvp =>
            Scalar(kvp.Key).Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string Scalar(YamlNode? node)
        => node switch
        {
            null => string.Empty,
            YamlScalarNode scalar => scalar.Value ?? string.Empty,
            _ => string.Empty
        };
}

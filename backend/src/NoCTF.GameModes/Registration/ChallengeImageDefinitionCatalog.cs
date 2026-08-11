using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Application.Challenges.Images;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;

namespace NoCTF.GameModes.Registration;

public sealed class ChallengeImageDefinitionCatalog : IChallengeImageDefinitionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ChallengeImageDefinitionReadResult Read(GameMode mode, string definitionJson)
    {
        try
        {
            var images = new List<ChallengeImageReference>();
            switch (mode)
            {
                case GameMode.Ctf:
                    AddRuntime(CtfConfigurationUpgrader.ParseChallenge(definitionJson).Runtime, images);
                    break;
                case GameMode.Awd:
                {
                    var configuration = AwdConfigurationUpgrader.ParseChallenge(definitionJson);
                    AddRuntime(configuration.Runtime, images);
                    AddChecker(configuration.Checker?.Job, images);
                    break;
                }
                case GameMode.Awdp:
                {
                    var configuration = AwdpConfigurationParser.ParseChallenge(definitionJson);
                    AddRuntime(configuration.Runtime, images);
                    AddChecker(configuration.Checker, images);
                    break;
                }
                case GameMode.Koh:
                    AddRuntime(KohConfigurationUpgrader.ParseChallenge(definitionJson).Runtime, images);
                    break;
                default:
                    return new(null, "Unsupported game mode.");
            }
            return new(images);
        }
        catch (Exception exception) when (exception is
            GameModeConfigurationException or JsonException or InvalidOperationException)
        {
            return new(null, exception.Message);
        }
    }

    public string Replace(
        GameMode mode,
        string definitionJson,
        IReadOnlyDictionary<ChallengeImageLocation, string> replacements)
    {
        if (replacements.Count == 0)
            return definitionJson;
        var read = Read(mode, definitionJson);
        if (!read.Succeeded)
            throw new InvalidOperationException(read.Error);
        if (read.Images!.All(image =>
                !replacements.TryGetValue(image.Location, out var replacement)
                || string.Equals(image.Image, replacement, StringComparison.Ordinal)))
            return definitionJson;

        var root = JsonNode.Parse(definitionJson)?.AsObject()
            ?? throw new InvalidOperationException("DefinitionJson must be a JSON object.");
        var runtime = GetObject(root, "runtime", required: false);
        if (runtime is not null)
        {
            var definition = GetObject(runtime, "definition", required: true)!;
            if (replacements.TryGetValue(
                    new(ChallengeImageLocationKind.RuntimeContainer),
                    out var containerImage))
                SetString(definition, "image", containerImage);

            var composeReplacements = replacements
                .Where(pair => pair.Key.Kind == ChallengeImageLocationKind.RuntimeComposeService)
                .ToDictionary(
                    pair => pair.Key.ServiceName
                        ?? throw new InvalidOperationException("Compose image location requires a service name."),
                    pair => pair.Value,
                    StringComparer.Ordinal);
            if (composeReplacements.Count > 0)
            {
                var composeYaml = GetString(definition, "composeYaml");
                SetString(
                    definition,
                    "composeYaml",
                    ComposeRuntimeDefinitionPolicy.ReplaceServiceImages(
                        composeYaml,
                        composeReplacements));
            }
        }

        if (replacements.TryGetValue(
                new(ChallengeImageLocationKind.Checker),
                out var checkerImage))
        {
            var checker = GetObject(root, "checker", required: true)!;
            if (mode == GameMode.Awd)
                checker = GetObject(checker, "job", required: true)!;
            SetString(checker, "image", checkerImage);
        }
        return root.ToJsonString(JsonOptions);
    }

    private static void AddRuntime(
        ChallengeRuntimeTemplate? runtime,
        ICollection<ChallengeImageReference> images)
    {
        switch (runtime?.Definition)
        {
            case ContainerRuntimeDefinition container:
                images.Add(new(
                    new(ChallengeImageLocationKind.RuntimeContainer),
                    container.Image));
                break;
            case ComposeRuntimeDefinition compose:
                foreach (var image in ComposeRuntimeDefinitionPolicy
                             .ReadServiceImages(compose.ComposeYaml)
                             .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    images.Add(new(
                        new(
                            ChallengeImageLocationKind.RuntimeComposeService,
                            image.Key),
                        image.Value));
                }
                break;
        }
    }

    private static void AddChecker(
        RunnerJobConfiguration? checker,
        ICollection<ChallengeImageReference> images)
    {
        if (checker is not null)
        {
            images.Add(new(
                new(ChallengeImageLocationKind.Checker),
                checker.Image));
        }
    }

    private static JsonObject? GetObject(
        JsonObject parent,
        string name,
        bool required)
    {
        var property = parent.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        if (property.Value is JsonObject value)
            return value;
        if (!required && property.Key is null)
            return null;
        throw new InvalidOperationException($"DefinitionJson field '{name}' must be an object.");
    }

    private static string GetString(JsonObject parent, string name)
    {
        var property = parent.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
            return text;
        throw new InvalidOperationException($"DefinitionJson field '{name}' must be a string.");
    }

    private static void SetString(JsonObject parent, string name, string value)
    {
        var property = parent.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        if (property.Key is null)
            throw new InvalidOperationException($"DefinitionJson field '{name}' was not found.");
        parent[property.Key] = value;
    }
}

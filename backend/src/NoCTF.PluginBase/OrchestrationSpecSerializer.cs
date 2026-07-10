using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoCTF.PluginBase;

public static class OrchestrationSpecSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static OrchestrationSpec Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new OrchestrationSpec();

        try
        {
            return JsonSerializer.Deserialize<OrchestrationSpec>(json, JsonOptions) ?? new OrchestrationSpec();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Orchestration JSON is invalid.", ex);
        }
    }

    public static string Write(OrchestrationSpec spec)
        => JsonSerializer.Serialize(spec, JsonOptions);

    public static OrchestrationSpec FromLegacy(
        string? image,
        string? command,
        int? exposedPort,
        string? composeYaml,
        string? composeProjectName,
        bool dockerCompose)
        => new()
        {
            Provider = OrchestrationProvider.Docker,
            Runtime = dockerCompose ? OrchestrationRuntimeKind.Compose : OrchestrationRuntimeKind.SingleContainer,
            Image = image,
            Command = command,
            ExposedPort = exposedPort,
            ComposeYaml = composeYaml,
            ComposeProjectName = composeProjectName,
            Kubernetes = new KubernetesOrchestrationSpec
            {
                Exposure = exposedPort is > 0
                    ? OrchestrationExposureType.NodePort
                    : OrchestrationExposureType.None
            }
        };
}

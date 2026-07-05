using System.Text.Json;
using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

internal static class ChallengeTemplateRequestRules
{
    private const int MaxJsonBytes = 64 * 1024;

    public static bool UsesRuntimeContainer(ChallengeDeploymentType deploymentType)
        => deploymentType is ChallengeDeploymentType.DynamicContainer or ChallengeDeploymentType.StaticContainer;

    public static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static ChallengeDeploymentType ResolveDeploymentType(ChallengeDeploymentType requested, string? attachmentUrl)
    {
        if (UsesRuntimeContainer(requested)) return requested;

        return string.IsNullOrWhiteSpace(attachmentUrl)
            ? ChallengeDeploymentType.NoAttachment
            : ChallengeDeploymentType.StaticAttachment;
    }

    public static string BuildOrchestrationJson(
        string? orchestrationJson,
        string? image,
        int? exposedPort,
        string? composeYaml,
        string? composeProjectName,
        ChallengeContainerMode containerMode)
    {
        var spec = !string.IsNullOrWhiteSpace(orchestrationJson)
            ? OrchestrationSpecSerializer.Read(orchestrationJson)
            : OrchestrationSpecSerializer.FromLegacy(
                image,
                null,
                exposedPort,
                composeYaml,
                composeProjectName,
                containerMode == ChallengeContainerMode.DockerCompose);

        spec.Runtime = containerMode == ChallengeContainerMode.DockerCompose
            ? OrchestrationRuntimeKind.Compose
            : spec.Runtime;
        if (!string.IsNullOrWhiteSpace(image))
            spec.Image = image;
        if (exposedPort is > 0)
            spec.ExposedPort = exposedPort;
        if (!string.IsNullOrWhiteSpace(composeYaml))
            spec.ComposeYaml = composeYaml;
        if (!string.IsNullOrWhiteSpace(composeProjectName))
            spec.ComposeProjectName = composeProjectName;

        return OrchestrationSpecSerializer.Write(spec);
    }

    public static string? ResolveImage(string orchestrationJson, string? legacyImage)
    {
        var spec = OrchestrationSpecSerializer.Read(orchestrationJson);
        return CleanOptional(legacyImage) ?? CleanOptional(spec.Image);
    }

    public static bool IsValidJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return true;

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}

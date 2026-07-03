using NoCTF.Core;

namespace NoCTF.API.Endpoints.Admin;

internal static class ChallengeTemplateRequestRules
{
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
}

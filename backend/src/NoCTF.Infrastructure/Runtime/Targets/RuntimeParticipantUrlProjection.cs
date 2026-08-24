using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Runtime.Targets;

public static class RuntimeParticipantUrlProjection
{
    public static IReadOnlyList<string> Filter(
        IChallengeRuntimeTemplateCatalog templates,
        GameMode mode,
        string definitionJson,
        IReadOnlyList<string> urls)
    {
        var bindings = templates.Get(mode, definitionJson)?.UrlBindings ?? [];
        return bindings
            .Select((binding, index) => (binding, index))
            .Where(item => item.binding.Exposure == RuntimeExposure.Participants
                && item.index < urls.Count)
            .Select(item => urls[item.index])
            .ToArray();
    }
}

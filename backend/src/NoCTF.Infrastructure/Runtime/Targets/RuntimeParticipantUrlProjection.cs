using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Runtime.Instances;

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

    public static IReadOnlyList<RuntimeAccessEndpointView> Filter(
        IChallengeRuntimeTemplateCatalog templates,
        GameMode mode,
        string definitionJson,
        IReadOnlyList<RuntimeAccessEndpointView> endpoints)
    {
        var bindings = templates.Get(mode, definitionJson)?.UrlBindings ?? [];
        var participantIndexes = bindings
            .Select((binding, index) => (binding, index))
            .Where(item => item.binding.Exposure == RuntimeExposure.Participants)
            .Select(item => item.index)
            .ToHashSet();
        return endpoints
            .Where(endpoint => participantIndexes.Contains(endpoint.BindingIndex))
            .OrderBy(endpoint => endpoint.BindingIndex)
            .ToArray();
    }
}

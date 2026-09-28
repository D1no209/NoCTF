using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Runtime.Targets;

public static class RuntimeParticipantUrlProjection
{
    public static IReadOnlyList<RuntimeAccessEndpointView> Filter(
        IChallengeRuntimeTemplateCatalog templates,
        ChallengeDefinition? definition,
        IReadOnlyList<RuntimeAccessEndpointView> endpoints)
    {
        var bindings = templates.Get(definition)?.UrlBindings ?? [];
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

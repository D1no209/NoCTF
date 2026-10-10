namespace NoCTF.Domain.Challenges;

public static class ChallengeDefinitionGraph
{
    public static void AssignChallengeId(ChallengeDefinition definition, Guid challengeId)
    {
        definition.ChallengeId = challengeId;
        if (definition.Checker is not null)
            definition.Checker.ChallengeId = challengeId;
        foreach (var item in definition.StringItems)
            item.ChallengeId = challengeId;
        if (definition.Runtime is null) return;
        definition.Runtime.ChallengeId = challengeId;
        foreach (var item in definition.Runtime.UrlBindings) item.ChallengeId = challengeId;
        if (definition.Runtime is ContainerChallengeRuntimeTemplate container)
            foreach (var service in container.Services)
            {
                service.ChallengeId = challengeId;
                foreach (var item in service.Commands) { item.ChallengeId = challengeId; item.ServiceName = service.Name; }
                foreach (var item in service.Environment) { item.ChallengeId = challengeId; item.ServiceName = service.Name; }
                foreach (var item in service.InternalPorts) { item.ChallengeId = challengeId; item.ServiceName = service.Name; }
            }
    }
}

public static class ChallengeDefinitionStructuralComparer
{
    public static bool Equals(ChallengeDefinition? left, ChallengeDefinition? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.GetType() != right.GetType()) return false;
        if (left.PatchEntrypoint != right.PatchEntrypoint
            || left.PatchTimeoutSeconds != right.PatchTimeoutSeconds
            || left.ReadyTimeoutSeconds != right.ReadyTimeoutSeconds
            || left.MaximumPatchUploadBytes != right.MaximumPatchUploadBytes
            || left.CheckerFixInput != right.CheckerFixInput
            || left.HasFlagTemplate != right.HasFlagTemplate
            || left.FlagTemplate.Header != right.FlagTemplate.Header
            || left.FlagTemplate.BodyTemplate != right.FlagTemplate.BodyTemplate
            || left.FlagTemplate.LeetLiteralText != right.FlagTemplate.LeetLiteralText
            || !CheckerEquals(left.Checker, right.Checker)
            || !SequenceEqual(
                left.StringItems,
                right.StringItems,
                item => (item.Kind, item.Position, item.Key, item.Value))
            || !RuntimeEquals(left.Runtime, right.Runtime))
            return false;
        return (left, right) switch
        {
            (CtfChallengeDefinition a, CtfChallengeDefinition b) =>
                a.InteractionKind == b.InteractionKind,
            (AwdChallengeDefinition a, AwdChallengeDefinition b) =>
                a.FlagInjectionCommand == b.FlagInjectionCommand
                && a.FlagInjectionTimeoutSeconds == b.FlagInjectionTimeoutSeconds
                && a.FlagInjectionServiceName == b.FlagInjectionServiceName,
            (AwdpChallengeDefinition, AwdpChallengeDefinition) => true,
            (KohChallengeDefinition, KohChallengeDefinition) => true,
            (NoCTF.Domain.LiveSolo.LiveSoloChallengeDefinition, NoCTF.Domain.LiveSolo.LiveSoloChallengeDefinition) => true,
            _ => false
        };
    }

    private static bool CheckerEquals(
        ChallengeCheckerDefinition? left,
        ChallengeCheckerDefinition? right) =>
        ReferenceEquals(left, right)
        || left is not null && right is not null
        && left.Image == right.Image
        && left.TimeoutSeconds == right.TimeoutSeconds
        && left.TargetServiceName == right.TargetServiceName;

    private static bool RuntimeEquals(
        ChallengeRuntimeTemplateEntity? left,
        ChallengeRuntimeTemplateEntity? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.GetType() != right.GetType()) return false;
        if (left.Allocation != right.Allocation
            || left.TtlSeconds != right.TtlSeconds
            || left.OperationTimeoutSeconds != right.OperationTimeoutSeconds
            || left.FlagSource != right.FlagSource
            || left.EgressPolicy != right.EgressPolicy
            || !SequenceEqual(left.UrlBindings, right.UrlBindings, item => (
                item.Position, item.IsControlCheck, item.UrlTemplate, item.Exposure,
                item.ContainerPort, item.ServiceName, item.VmId, item.GuestPort))
)
            return false;
        return (left, right) switch
        {
            (ContainerChallengeRuntimeTemplate a, ContainerChallengeRuntimeTemplate b) =>
                a.Services.Count == b.Services.Count && a.Services.OrderBy(item => item.Position)
                    .Zip(b.Services.OrderBy(item => item.Position)).All(pair =>
                        pair.First.Name == pair.Second.Name && pair.First.Image == pair.Second.Image
                        && pair.First.CpuCores == pair.Second.CpuCores && pair.First.MemoryMiB == pair.Second.MemoryMiB
                        && pair.First.FlagEnvironmentVariableName == pair.Second.FlagEnvironmentVariableName
                        && SequenceEqual(pair.First.Commands, pair.Second.Commands, item => (item.IsArgument, item.Position, item.Value))
                        && SequenceEqual(pair.First.Environment, pair.Second.Environment, item => (item.Name, item.Value))
                        && SequenceEqual(pair.First.InternalPorts, pair.Second.InternalPorts, item => item.Port)),
            (OvaChallengeRuntimeTemplate a, OvaChallengeRuntimeTemplate b) =>
                a.OvaSourceUrl == b.OvaSourceUrl && a.Sha256 == b.Sha256
                && a.Limits.MemoryBytes == b.Limits.MemoryBytes && a.Limits.CpuMillicores == b.Limits.CpuMillicores
                && a.Limits.PidsLimit == b.Limits.PidsLimit && a.HasExplicitLimits == b.HasExplicitLimits,
            _ => false
        };
    }

    private static bool SequenceEqual<T, TKey>(
        IEnumerable<T> left,
        IEnumerable<T> right,
        Func<T, TKey> key) where TKey : notnull =>
        left.Select(key).OrderBy(value => value).SequenceEqual(
            right.Select(key).OrderBy(value => value));
}

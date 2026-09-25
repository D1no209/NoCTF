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
        foreach (var item in definition.Runtime.KeyValues) item.ChallengeId = challengeId;
        foreach (var item in definition.Runtime.CommandItems) item.ChallengeId = challengeId;
        if (definition.Runtime is ContainerChallengeRuntimeTemplate container)
        {
            foreach (var item in container.Capabilities) item.ChallengeId = challengeId;
            foreach (var item in container.PortMappings) item.ChallengeId = challengeId;
            foreach (var item in container.InternalPorts) item.ChallengeId = challengeId;
        }
        else if (definition.Runtime is ComposeChallengeRuntimeTemplate compose)
        {
            foreach (var item in compose.ServiceResources) item.ChallengeId = challengeId;
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
            || left.CheckerAllowRoot != right.CheckerAllowRoot
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
            || left.Limits.MemoryBytes != right.Limits.MemoryBytes
            || left.Limits.NanoCpus != right.Limits.NanoCpus
            || left.Limits.PidsLimit != right.Limits.PidsLimit
            || left.HasExplicitLimits != right.HasExplicitLimits
            || left.TtlSeconds != right.TtlSeconds
            || left.OperationTimeoutSeconds != right.OperationTimeoutSeconds
            || left.FlagSource != right.FlagSource
            || left.EgressPolicy != right.EgressPolicy
            || !SequenceEqual(left.UrlBindings, right.UrlBindings, item => (
                item.Position, item.IsControlCheck, item.UrlTemplate, item.Exposure,
                item.ContainerPort, item.ServiceName, item.VmId, item.GuestPort))
            || !SequenceEqual(left.KeyValues, right.KeyValues,
                item => (item.Kind, item.Key, item.Value))
            || !SequenceEqual(left.CommandItems, right.CommandItems,
                item => (item.Position, item.Value)))
            return false;
        return (left, right) switch
        {
            (ContainerChallengeRuntimeTemplate a, ContainerChallengeRuntimeTemplate b) =>
                a.Image == b.Image
                && a.FlagEnvironmentVariableName == b.FlagEnvironmentVariableName
                && a.Security.NoNewPrivileges == b.Security.NoNewPrivileges
                && a.Security.ReadonlyRootfs == b.Security.ReadonlyRootfs
                && a.Security.RunAsNonRoot == b.Security.RunAsNonRoot
                && SequenceEqual(a.Capabilities, b.Capabilities, item => (item.Add, item.Name))
                && SequenceEqual(a.PortMappings, b.PortMappings,
                    item => (item.ContainerPort, item.HostPort))
                && SequenceEqual(a.InternalPorts, b.InternalPorts, item => item.Port),
            (ComposeChallengeRuntimeTemplate a, ComposeChallengeRuntimeTemplate b) =>
                a.ComposeYaml == b.ComposeYaml
                && SequenceEqual(a.ServiceResources, b.ServiceResources, item => (
                    item.ServiceName, item.Limits.MemoryBytes,
                    item.Limits.NanoCpus, item.Limits.PidsLimit)),
            (OvaChallengeRuntimeTemplate a, OvaChallengeRuntimeTemplate b) =>
                a.OvaSourceUrl == b.OvaSourceUrl && a.Sha256 == b.Sha256,
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

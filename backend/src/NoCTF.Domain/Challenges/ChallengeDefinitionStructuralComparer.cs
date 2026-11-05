using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Domain.Challenges;

public static class ChallengeDefinitionGraph
{
    public static void AssignChallengeId(ChallengeDefinition definition, Guid challengeId)
    {
        definition.ChallengeId = challengeId;
        foreach (var capability in definition.Capabilities)
        {
            capability.ChallengeId = challengeId;
            switch (capability)
            {
                case BreakCapabilityDefinition value:
                    foreach (var store in value.FlagStores) store.ChallengeId = challengeId;
                    foreach (var target in value.EnvironmentTargets) target.ChallengeId = challengeId;
                    break;
                case FixCapabilityDefinition value:
                    foreach (var argument in value.Command) argument.ChallengeId = challengeId;
                    break;
                case CheckCapabilityDefinition value:
                    foreach (var argument in value.Command) argument.ChallengeId = challengeId;
                    foreach (var variable in value.Environment) variable.ChallengeId = challengeId;
                    foreach (var requirement in value.Requirements) requirement.ChallengeId = challengeId;
                    break;
                case QuestionnaireCapabilityDefinition value:
                    value.CurrentRevision.ChallengeId = challengeId;
                    value.CurrentRevisionId = value.CurrentRevision.Id;
                    foreach (var question in value.CurrentRevision.Questions)
                    {
                        question.RevisionId = value.CurrentRevisionId;
                        foreach (var option in question.Options) { option.RevisionId = question.RevisionId; option.QuestionId = question.Id; option.QuestionVersionId = question.VersionId; }
                        foreach (var item in question.Classifications) { item.RevisionId = question.RevisionId; item.QuestionId = question.Id; }
                        foreach (var file in question.Files) { file.RevisionId = question.RevisionId; file.QuestionId = question.Id; }
                    }
                    break;
            }
        }
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
        if (left is null || right is null || left.Capabilities.Count != right.Capabilities.Count) return false;
        return RuntimeEquals(left.Runtime, right.Runtime)
            && left.Capabilities.OrderBy(item => item.Kind).Zip(right.Capabilities.OrderBy(item => item.Kind))
                .All(pair => CapabilityEquals(pair.First, pair.Second));
    }

    private static bool CapabilityEquals(ChallengeCapabilityDefinition left, ChallengeCapabilityDefinition right) =>
        (left, right) switch
        {
            (BreakCapabilityDefinition a, BreakCapabilityDefinition b) =>
                a.HasFlagTemplate == b.HasFlagTemplate && a.FlagTemplate.Header == b.FlagTemplate.Header
                && a.FlagTemplate.BodyTemplate == b.FlagTemplate.BodyTemplate && a.FlagTemplate.LeetLiteralText == b.FlagTemplate.LeetLiteralText
                && a.InjectionCommand == b.InjectionCommand && a.InjectionTimeoutSeconds == b.InjectionTimeoutSeconds
                && a.InjectionServiceName == b.InjectionServiceName
                && SequenceEqual(a.EnvironmentTargets, b.EnvironmentTargets, target => (target.Position, target.ServiceName, target.VariableName))
                && SequenceEqual(a.FlagStores, b.FlagStores,
                    store => (store.Id, store.Position, store.Name, store.DeliveryKind,
                        store.TargetServiceName, store.InjectionCommand, store.InjectionTimeoutSeconds, store.PreservesHistoricalFlags)),
            (FixCapabilityDefinition a, FixCapabilityDefinition b) =>
                a.Entrypoint == b.Entrypoint && a.TargetServiceName == b.TargetServiceName
                && a.TimeoutSeconds == b.TimeoutSeconds && a.MaximumUploadBytes == b.MaximumUploadBytes
                && SequenceEqual(a.Command, b.Command, item => (item.Position, item.Value)),
            (CheckCapabilityDefinition a, CheckCapabilityDefinition b) =>
                a.Image == b.Image && a.TargetServiceName == b.TargetServiceName && a.TimeoutSeconds == b.TimeoutSeconds
                && a.ReadyTimeoutSeconds == b.ReadyTimeoutSeconds && a.AcceptsFixInput == b.AcceptsFixInput
                && SequenceEqual(a.Command, b.Command, item => (item.Position, item.Value))
                && SequenceEqual(a.Environment, b.Environment, item => (item.Name, item.Value))
                && SequenceEqual(a.Requirements, b.Requirements, item => item.Kind),
            (ControlCapabilityDefinition a, ControlCapabilityDefinition b) =>
                a.Protocol == b.Protocol && a.UrlTemplate == b.UrlTemplate && a.ServiceName == b.ServiceName
                && a.ContainerPort == b.ContainerPort && a.VmId == b.VmId && a.GuestPort == b.GuestPort
                && a.TimeoutSeconds == b.TimeoutSeconds,
            (QuestionnaireCapabilityDefinition a, QuestionnaireCapabilityDefinition b) =>
                a.CurrentRevisionId == b.CurrentRevisionId && QuestionnaireContentEquals(a.CurrentRevision, b.CurrentRevision),
            _ => false
        };

    public static bool QuestionnaireContentEquals(QuestionnaireRevision left, QuestionnaireRevision right) =>
        left.Questions.Count == right.Questions.Count && left.Questions.OrderBy(question => question.Position)
            .Zip(right.Questions.OrderBy(question => question.Position)).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.SourceId == pair.Second.SourceId
                && pair.First.Position == pair.Second.Position && pair.First.Kind == pair.Second.Kind
                && pair.First.Difficulty == pair.Second.Difficulty && pair.First.Title == pair.Second.Title
                && pair.First.Body == pair.Second.Body && pair.First.Explanation == pair.Second.Explanation
                && SequenceEqual(pair.First.Options, pair.Second.Options,
                    option => (option.Id, option.Position, option.Content, option.IsCorrect, option.TruthValue))
                && SequenceEqual(pair.First.Classifications, pair.Second.Classifications,
                    item => (item.Kind, item.Name, item.NormalizedName))
                && SequenceEqual(pair.First.Files, pair.Second.Files, file => file.FileId));

    public static bool HasExecutionChanges(ChallengeDefinition left, ChallengeDefinition right) =>
        HasRuntimeChanges(left, right) || ChangedExecutionCapabilities(left, right).Count > 0;

    public static bool HasRuntimeChanges(ChallengeDefinition left, ChallengeDefinition right) => !RuntimeEquals(left.Runtime, right.Runtime);

    public static IReadOnlyList<ChallengeCapabilityKind> ChangedExecutionCapabilities(ChallengeDefinition left, ChallengeDefinition right) =>
        left.Capabilities.Where(capability => capability.Kind != ChallengeCapabilityKind.Questionnaire
                && (right.Capabilities.SingleOrDefault(value => value.Kind == capability.Kind) is not { } replacement
                    || !CapabilityEquals(capability, replacement)))
            .Select(capability => capability.Kind).ToArray();

    private static bool RuntimeEquals(
        ChallengeRuntimeTemplateEntity? left,
        ChallengeRuntimeTemplateEntity? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.GetType() != right.GetType()) return false;
        if (left.OperationTimeoutSeconds != right.OperationTimeoutSeconds
            || left.EgressPolicy != right.EgressPolicy
            || !SequenceEqual(left.UrlBindings, right.UrlBindings, item => (
                item.Position, item.UrlTemplate, item.Exposure,
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

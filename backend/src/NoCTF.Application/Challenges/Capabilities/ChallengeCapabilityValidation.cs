using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public enum CapabilityConfigurationFailure : short
{
    InvalidCapabilitySet, RuntimeRequired, TargetServiceMissing, InvalidCommand, InvalidTimeout,
    InvalidUploadLimit, InvalidChecker, InvalidRequirements, InvalidControl, InvalidQuestionnaire,
    InvalidQuestion, InvalidOptions, DuplicateIdentity, InvalidOrder, InvalidEnvironmentTarget
}

public sealed record CapabilityConfigurationError(ChallengeCapabilityKind? Capability, CapabilityConfigurationFailure Code);

public static class ChallengeCapabilityValidation
{
    public static IReadOnlyList<CapabilityConfigurationError> Validate(ChallengeDefinition definition)
    {
        var errors = new List<CapabilityConfigurationError>();
        if (definition.Capabilities.Count is < 1 or > 5
            || definition.Capabilities.Any(value => !Enum.IsDefined(value.Kind))
            || definition.Capabilities.Select(value => value.Kind).Distinct().Count() != definition.Capabilities.Count)
            return [new(null, CapabilityConfigurationFailure.InvalidCapabilitySet)];

        foreach (var capability in definition.Capabilities)
        {
            void Fail(CapabilityConfigurationFailure code) => errors.Add(new(capability.Kind, code));
            bool HasService(string? name) => definition.Runtime is ContainerChallengeRuntimeTemplate container
                && !string.IsNullOrWhiteSpace(name) && container.Services.Any(service => service.Name == name);
            switch (capability)
            {
                case BreakCapabilityDefinition value:
                    foreach (var target in value.EnvironmentTargets)
                    {
                        if (!HasService(target.ServiceName)) Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                        if (!NoCTF.Application.Runtime.Configuration.ContainerRuntimeDefinitionPolicy.IsEnvironmentName(target.VariableName)
                            || target.VariableName.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase))
                            Fail(CapabilityConfigurationFailure.InvalidEnvironmentTarget);
                    }
                    if (value.EnvironmentTargets.Select(target => target.ServiceName).Distinct().Count() != value.EnvironmentTargets.Count)
                        Fail(CapabilityConfigurationFailure.DuplicateIdentity);
                    if (!Ordered(value.EnvironmentTargets.Select(target => target.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                    if (value.InjectionCommand is not null && !HasService(value.InjectionServiceName))
                        Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                    if (value.InjectionTimeoutSeconds is < 1 or > 3600) Fail(CapabilityConfigurationFailure.InvalidTimeout);
                    if (value.FlagStores.Select(store => store.Id).Distinct().Count() != value.FlagStores.Count
                        || value.FlagStores.Any(store => store.Id == Guid.Empty)) Fail(CapabilityConfigurationFailure.DuplicateIdentity);
                    foreach (var store in value.FlagStores)
                    {
                        if (!HasService(store.TargetServiceName)) Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                        if (!Enum.IsDefined(store.DeliveryKind) || string.IsNullOrWhiteSpace(store.Name)
                            || store.DeliveryKind == FlagDeliveryKind.RunnerCommand && string.IsNullOrWhiteSpace(store.InjectionCommand))
                            Fail(CapabilityConfigurationFailure.InvalidCommand);
                        if (store.InjectionTimeoutSeconds is < 1 or > 3600) Fail(CapabilityConfigurationFailure.InvalidTimeout);
                    }
                    if (!Ordered(value.FlagStores.Select(store => store.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                    break;
                case FixCapabilityDefinition value:
                    if (definition.Runtime is not ContainerChallengeRuntimeTemplate) Fail(CapabilityConfigurationFailure.RuntimeRequired);
                    else if (!HasService(value.TargetServiceName)) Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                    if (!FixCommand.IsValid(value))
                        Fail(CapabilityConfigurationFailure.InvalidCommand);
                    if (!Ordered(value.Command.Select(argument => argument.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                    if (value.TimeoutSeconds is < 1 or > 3600) Fail(CapabilityConfigurationFailure.InvalidTimeout);
                    if (value.MaximumUploadBytes is < 1 or > 1073741824) Fail(CapabilityConfigurationFailure.InvalidUploadLimit);
                    break;
                case CheckCapabilityDefinition value:
                    if (!HasService(value.TargetServiceName)) Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                    if (string.IsNullOrWhiteSpace(value.Image)
                        || value.Command.Any(argument => string.IsNullOrWhiteSpace(argument.Value))
                        || value.Environment.Any(variable => string.IsNullOrWhiteSpace(variable.Name))
                        || value.Environment.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count() != value.Environment.Count)
                        Fail(CapabilityConfigurationFailure.InvalidChecker);
                    if (value.TimeoutSeconds is < 1 or > 3600 || value.ReadyTimeoutSeconds is < 1 or > 3600)
                        Fail(CapabilityConfigurationFailure.InvalidTimeout);
                    if (!Ordered(value.Command.Select(argument => argument.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                    if (value.Requirements.Count == 0 || value.Requirements.Any(item => !Enum.IsDefined(item.Kind))
                        || value.Requirements.Select(item => item.Kind).Distinct().Count() != value.Requirements.Count)
                        Fail(CapabilityConfigurationFailure.InvalidRequirements);
                    break;
                case ControlCapabilityDefinition value:
                    if (definition.Runtime is null) Fail(CapabilityConfigurationFailure.RuntimeRequired);
                    if (!Enum.IsDefined(value.Protocol) || string.IsNullOrWhiteSpace(value.UrlTemplate))
                        Fail(CapabilityConfigurationFailure.InvalidControl);
                    if (definition.Runtime is ContainerChallengeRuntimeTemplate && !HasService(value.ServiceName))
                        Fail(CapabilityConfigurationFailure.TargetServiceMissing);
                    if (value.TimeoutSeconds is < 1 or > 3600) Fail(CapabilityConfigurationFailure.InvalidTimeout);
                    break;
                case QuestionnaireCapabilityDefinition value:
                    if (value.CurrentRevision is not { } revision || revision.Id == Guid.Empty
                        || value.CurrentRevisionId != revision.Id || revision.Number < 1 || revision.Questions.Count == 0)
                    {
                        Fail(CapabilityConfigurationFailure.InvalidQuestionnaire);
                        break;
                    }
                    if (!Ordered(revision.Questions.Select(question => question.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                    if (revision.Questions.Select(question => question.Id).Distinct().Count() != revision.Questions.Count)
                        Fail(CapabilityConfigurationFailure.DuplicateIdentity);
                    foreach (var question in revision.Questions)
                    {
                        if (question.Id == Guid.Empty || question.SourceId == Guid.Empty || !Enum.IsDefined(question.Kind)
                            || !Enum.IsDefined(question.Difficulty) || string.IsNullOrWhiteSpace(question.Title) || question.Title.Length > 160
                            || string.IsNullOrWhiteSpace(question.Body) || question.Body.Length > 64000 || question.Explanation?.Length > 64000)
                            Fail(CapabilityConfigurationFailure.InvalidQuestion);
                        var correct = question.Options.Count(option => option.IsCorrect);
                        if (question.Options.Count is < 2 or > 8 || correct == 0
                            || question.Kind == QuestionnaireQuestionKind.MultipleChoice && correct < 2
                            || question.Kind != QuestionnaireQuestionKind.MultipleChoice && correct != 1
                            || question.Options.Any(option => option.Id == Guid.Empty || string.IsNullOrWhiteSpace(option.Content) || option.Content.Length > 16000)
                            || question.Options.Select(option => option.Id).Distinct().Count() != question.Options.Count
                            || question.Kind == QuestionnaireQuestionKind.TrueFalse && (question.Options.Count != 2
                                || !question.Options.Select(option => option.TruthValue).ToHashSet().SetEquals([true, false])))
                            Fail(CapabilityConfigurationFailure.InvalidOptions);
                        if (!Ordered(question.Options.Select(option => option.Position))) Fail(CapabilityConfigurationFailure.InvalidOrder);
                        if (question.Classifications.Any(item => !Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.Name)
                                || item.Name.Length > 80 || item.NormalizedName != item.Name.Trim().ToUpperInvariant())
                            || question.Files.Any(file => file.FileId == Guid.Empty))
                            Fail(CapabilityConfigurationFailure.InvalidQuestion);
                        if (question.Classifications.Select(item => (item.Kind, item.NormalizedName)).Distinct().Count() != question.Classifications.Count
                            || question.Files.Select(file => file.FileId).Distinct().Count() != question.Files.Count)
                            Fail(CapabilityConfigurationFailure.DuplicateIdentity);
                    }
                    break;
                default:
                    Fail(CapabilityConfigurationFailure.InvalidCapabilitySet);
                    break;
            }
        }
        return errors.Distinct().ToArray();
    }

    private static bool Ordered(IEnumerable<int> positions)
    {
        var ordered = positions.Order().ToArray();
        return ordered.SequenceEqual(Enumerable.Range(0, ordered.Length));
    }
}

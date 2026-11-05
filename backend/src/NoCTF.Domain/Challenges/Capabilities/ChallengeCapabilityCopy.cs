namespace NoCTF.Domain.Challenges.Capabilities;

public static class ChallengeCapabilityCopy
{
    public static ChallengeCapabilityDefinition Copy(ChallengeCapabilityDefinition source) => source switch
    {
        BreakCapabilityDefinition value => new BreakCapabilityDefinition
        {
            HasFlagTemplate = value.HasFlagTemplate,
            FlagTemplate = new() { Header = value.FlagTemplate.Header, BodyTemplate = value.FlagTemplate.BodyTemplate, LeetLiteralText = value.FlagTemplate.LeetLiteralText },
            InjectionCommand = value.InjectionCommand, InjectionTimeoutSeconds = value.InjectionTimeoutSeconds, InjectionServiceName = value.InjectionServiceName,
            EnvironmentTargets = value.EnvironmentTargets.Select(target => new FlagEnvironmentTarget
                { Position = target.Position, ServiceName = target.ServiceName, VariableName = target.VariableName }).ToList(),
            FlagStores = value.FlagStores.Select(store => new FlagStoreDefinition
            {
                Id = store.Id, Position = store.Position, Name = store.Name, TargetServiceName = store.TargetServiceName,
                DeliveryKind = store.DeliveryKind, InjectionCommand = store.InjectionCommand,
                InjectionTimeoutSeconds = store.InjectionTimeoutSeconds, PreservesHistoricalFlags = store.PreservesHistoricalFlags
            }).ToList()
        },
        FixCapabilityDefinition value => new FixCapabilityDefinition
        {
            TargetServiceName = value.TargetServiceName, Entrypoint = value.Entrypoint, TimeoutSeconds = value.TimeoutSeconds,
            MaximumUploadBytes = value.MaximumUploadBytes,
            Command = value.Command.Select(argument => new FixCommandArgument { Position = argument.Position, Value = argument.Value }).ToList()
        },
        CheckCapabilityDefinition value => new CheckCapabilityDefinition
        {
            Image = value.Image, TargetServiceName = value.TargetServiceName, TimeoutSeconds = value.TimeoutSeconds,
            ReadyTimeoutSeconds = value.ReadyTimeoutSeconds, AcceptsFixInput = value.AcceptsFixInput,
            Command = value.Command.Select(argument => new CheckCommandArgument { Position = argument.Position, Value = argument.Value }).ToList(),
            Environment = value.Environment.Select(variable => new CheckEnvironmentVariable { Name = variable.Name, Value = variable.Value }).ToList(),
            Requirements = value.Requirements.Select(requirement => new CheckRequirement { Kind = requirement.Kind }).ToList()
        },
        ControlCapabilityDefinition value => new ControlCapabilityDefinition
        {
            Protocol = value.Protocol, UrlTemplate = value.UrlTemplate, ServiceName = value.ServiceName,
            ContainerPort = value.ContainerPort, VmId = value.VmId, GuestPort = value.GuestPort, TimeoutSeconds = value.TimeoutSeconds
        },
        QuestionnaireCapabilityDefinition value => CopyQuestionnaire(value),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source.GetType(), "Unknown capability definition.")
    };

    private static QuestionnaireCapabilityDefinition CopyQuestionnaire(QuestionnaireCapabilityDefinition source)
    {
        var revision = new QuestionnaireRevision
        {
            Id = Guid.CreateVersion7(), Number = 1, CreatedAt = source.CurrentRevision.CreatedAt,
            Questions = source.CurrentRevision.Questions.Select(question => new QuestionnaireQuestion
            {
                Id = question.Id, SourceId = question.SourceId, Position = question.Position, Kind = question.Kind,
                Difficulty = question.Difficulty, Title = question.Title, Body = question.Body, Explanation = question.Explanation,
                Options = question.Options.Select(option => new QuestionnaireOption
                {
                    Id = option.Id, Position = option.Position, Content = option.Content, IsCorrect = option.IsCorrect, TruthValue = option.TruthValue
                }).ToList(),
                Classifications = question.Classifications.Select(item => new QuestionnaireClassification
                {
                    Kind = item.Kind, Name = item.Name, NormalizedName = item.NormalizedName
                }).ToList(),
                Files = question.Files.Select(file => new QuestionnaireFile { FileId = file.FileId }).ToList()
            }).ToList()
        };
        return new() { CurrentRevisionId = revision.Id, CurrentRevision = revision };
    }
}

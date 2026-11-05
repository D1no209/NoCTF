using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeVisibilityProtocol>))]
public enum ChallengeVisibilityProtocol { Private, Shared }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<SpecificationKindProtocol>))]
public enum SpecificationKindProtocol { Attachment, AwdRound, RuntimeDefinition, Hint, RuntimeInstance }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeCapabilityProtocol>))]
public enum ChallengeCapabilityProtocol { Break, Fix, Check, Control, Questionnaire }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeAllocationProtocol>))]
public enum RuntimeAllocationProtocol { Shared, PerTeam }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeFlagSourceProtocol>))]
public enum RuntimeFlagSourceProtocol { Static, PerTeam, AwdRotation }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeEgressPolicyProtocol>))]
public enum RuntimeEgressPolicyProtocol { Isolated, InternetOnly }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeExposureProtocol>))]
public enum RuntimeExposureProtocol { OwnerOnly, Participants }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeRuntimeKindProtocol>))]
public enum ChallengeRuntimeKindProtocol { Container, Ova }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<FlagDeliveryProtocol>))]
public enum FlagDeliveryProtocol { RunnerCommand, BusinessChecker }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<CheckItemProtocol>))]
public enum CheckItemProtocol { Connection, Functionality, Vulnerability, FlagWrite, CurrentFlagRead, HistoricalFlagRead }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ControlProtocolKind>))]
public enum ControlProtocolKind { HttpTeamIdentifier }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<QuestionnaireQuestionProtocol>))]
public enum QuestionnaireQuestionProtocol { SingleChoice, MultipleChoice, TrueFalse }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<QuestionnaireDifficultyProtocol>))]
public enum QuestionnaireDifficultyProtocol { Basic, Intermediate, Advanced }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<QuestionnaireClassificationProtocol>))]
public enum QuestionnaireClassificationProtocol { Knowledge, Tag }

public sealed record RuntimeResourceLimitsContract(long MemoryBytes, long CpuMillicores, long PidsLimit);
public sealed record RuntimeUrlBindingContract(string UrlTemplate, RuntimeExposureProtocol Exposure,
    int? ContainerPort, string? ServiceName, string? VmId, int? GuestPort);
public sealed class ChallengeRuntimeContract
{
    public required ChallengeRuntimeKindProtocol Kind { get; set; }
    public RuntimeResourceLimitsContract? Limits { get; set; }
    public int? OperationTimeoutSeconds { get; set; }
    public RuntimeEgressPolicyProtocol EgressPolicy { get; set; }
    public IReadOnlyList<RuntimeUrlBindingContract> UrlBindings { get; set; } = [];
    public ContainerChallengeRuntimeContract? Container { get; set; }
    public OvaChallengeRuntimeContract? Ova { get; set; }
}
public sealed class ContainerChallengeRuntimeContract { public required IReadOnlyList<RuntimeServiceContract> Services { get; set; } }
public sealed class RuntimeServiceContract
{
    public required string Name { get; set; }
    public required string Image { get; set; }
    public decimal CpuCores { get; set; } = 0.5m;
    public long MemoryMiB { get; set; } = 512;
    public IReadOnlyList<string> Command { get; set; } = [];
    public IReadOnlyList<string> Arguments { get; set; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<int> InternalPorts { get; set; } = [];
}
public sealed class OvaChallengeRuntimeContract
{
    public required string SourceUrl { get; set; }
    public required string Sha256 { get; set; }
}
public sealed record RunnerJobContract(string Image, IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment, int TimeoutSeconds, string? TargetServiceName);
public sealed record FlagInjectionContract(string Command, int TimeoutSeconds, string? ServiceName);
public sealed record FlagStoreContract(Guid Id, string Name, FlagDeliveryProtocol Delivery,
    string? TargetServiceName, string? InjectionCommand, int InjectionTimeoutSeconds, bool PreservesHistoricalFlags);

public sealed class ChallengeDefinitionContract
{
    public ChallengeRuntimeContract? Runtime { get; set; }
    public required IReadOnlyList<ChallengeCapabilityContract> Capabilities { get; set; }
}
public sealed class ChallengeCapabilityContract
{
    public required ChallengeCapabilityProtocol Kind { get; set; }
    public BreakCapabilityContract? Break { get; set; }
    public FixCapabilityContract? Fix { get; set; }
    public CheckCapabilityContract? Check { get; set; }
    public ControlCapabilityContract? Control { get; set; }
    public QuestionnaireCapabilityContract? Questionnaire { get; set; }
}
public sealed class BreakCapabilityContract
{
    public FlagTemplateContract? FlagTemplate { get; set; }
    public FlagInjectionContract? Injection { get; set; }
    public IReadOnlyList<FlagStoreContract> FlagStores { get; set; } = [];
    public IReadOnlyList<FlagEnvironmentTargetContract> EnvironmentTargets { get; set; } = [];
}
public sealed record FlagEnvironmentTargetContract(string ServiceName, string VariableName);
public sealed class FixCapabilityContract
{
    public required string TargetServiceName { get; set; }
    public string Entrypoint { get; set; } = "fix.sh";
    public IReadOnlyList<string> Command { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 60;
    public long MaximumUploadBytes { get; set; } = 256L * 1024 * 1024;
}
public sealed class CheckCapabilityContract
{
    public required RunnerJobContract Checker { get; set; }
    public int ReadyTimeoutSeconds { get; set; } = 30;
    public bool AcceptsFixInput { get; set; }
    public required IReadOnlyList<CheckItemProtocol> Requirements { get; set; }
}
public sealed record ControlCapabilityContract(ControlProtocolKind Protocol, string UrlTemplate,
    string? ServiceName, int? ContainerPort, string? VmId, int? GuestPort, int TimeoutSeconds);
public sealed record QuestionnaireCapabilityContract(Guid RevisionId, int RevisionNumber,
    IReadOnlyList<QuestionnaireQuestionContract> Questions);
public sealed record QuestionnaireQuestionContract(Guid Id, Guid SourceId, QuestionnaireQuestionProtocol Kind,
    QuestionnaireDifficultyProtocol Difficulty, string Title, string Body, string? Explanation,
    IReadOnlyList<QuestionnaireOptionContract> Options, IReadOnlyList<QuestionnaireClassificationContract> Classifications,
    IReadOnlyList<Guid> FileIds);
public sealed record QuestionnaireOptionContract(Guid Id, string Content, bool IsCorrect, bool? TruthValue);
public sealed record QuestionnaireClassificationContract(QuestionnaireClassificationProtocol Kind, string Name);

public sealed class CreateChallengeTemplateRequest
{
    public Guid? Id { get; set; }
    public ChallengeVisibilityProtocol Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public required ChallengeDefinitionContract Definition { get; set; }
}
public sealed record ChallengeTemplateResponse(Guid Id, Guid OwnerId, IReadOnlyList<Guid> ManagerIds,
    IReadOnlyList<ChallengeCapabilityProtocol> Capabilities, ChallengeVisibilityProtocol Visibility,
    string Title, string? Description, string Direction, ChallengeDefinitionContract Definition,
    DateTimeOffset? DeletedAt, int ActiveCompetitionReferenceCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public Guid ConcurrencyStamp { get; init; }
}
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeTemplateConflictCode>))]
public enum ChallengeTemplateConflictCode
{
    ResourceIdConflict, ReferencedCapabilityConflict, ActiveRuntimeDefinitionConflict,
    OwnerIncludedInManagerSet, UserNotFound, RoleNotEligible, PendingExecutionConflict
}
public sealed record ChallengeTemplateConflictResponse(
    [property: Required, JsonRequired] ChallengeTemplateConflictCode Code, string Detail,
    [property: Required, JsonRequired] IReadOnlyList<Guid> UserIds)
{
    [Required, JsonRequired]
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}
internal static class ChallengeTemplateWriteResponseMapper
{
    public static ChallengeTemplateConflictResponse ToConflict(ChallengeTemplateWriteResult result) => new(result.State switch
    {
        ChallengeTemplateWriteState.ResourceIdConflict => ChallengeTemplateConflictCode.ResourceIdConflict,
        ChallengeTemplateWriteState.ReferencedCapabilityConflict => ChallengeTemplateConflictCode.ReferencedCapabilityConflict,
        ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict => ChallengeTemplateConflictCode.ActiveRuntimeDefinitionConflict,
        ChallengeTemplateWriteState.OwnerIncludedInManagerSet => ChallengeTemplateConflictCode.OwnerIncludedInManagerSet,
        ChallengeTemplateWriteState.UserNotFound => ChallengeTemplateConflictCode.UserNotFound,
        ChallengeTemplateWriteState.RoleNotEligible => ChallengeTemplateConflictCode.RoleNotEligible,
        ChallengeTemplateWriteState.PendingExecutionConflict => ChallengeTemplateConflictCode.PendingExecutionConflict,
        _ => throw new InvalidOperationException($"Unsupported conflict {result.State}.")
    }, result.Detail ?? "The challenge template conflicts with its current references or execution state.", result.UserIds ?? []);
}
public sealed class CreateChallengeTemplateValidator : Validator<CreateChallengeTemplateRequest>
{
    public CreateChallengeTemplateValidator()
    {
        RuleFor(request => request.Id).Must(id => id is null || id != Guid.Empty);
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
        RuleFor(request => request.Definition).Must(ChallengeDefinitionContractMapper.HasValidShape);
    }
}
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class ChallengeTemplateMapper
{
    public static CreateChallengeTemplateCommand ToCommand(CreateChallengeTemplateRequest request, Guid ownerId, DateTimeOffset at) =>
        new(request.Id, ownerId, ToDomain(request.Visibility), request.Title, request.Description, request.Direction,
            ChallengeDefinitionContractMapper.ToDomain(request.Id ?? Guid.Empty, request.Definition), at);
    public static ChallengeTemplateResponse ToResponse(ChallengeTemplateView value) =>
        new(value.Id, value.OwnerId, value.ManagerIds, value.Capabilities.Select(item => (ChallengeCapabilityProtocol)item).ToArray(),
            ToProtocol(value.Visibility), value.Title, value.Description, value.Direction,
            ChallengeDefinitionContractMapper.FromDomain(value.Definition), value.DeletedAt, value.ActiveCompetitionReferenceCount,
            value.CreatedAt, value.UpdatedAt) { ConcurrencyStamp = value.ConcurrencyStamp };
    [MapEnum(EnumMappingStrategy.ByName)] public static partial ChallengeVisibility ToDomain(ChallengeVisibilityProtocol value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial ChallengeVisibilityProtocol ToProtocol(ChallengeVisibility value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial SpecificationKind ToDomain(SpecificationKindProtocol value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial SpecificationKindProtocol ToProtocol(SpecificationKind value);
}

public static class ChallengeDefinitionContractMapper
{
    public static bool HasValidShape(ChallengeDefinitionContract? contract) => contract is { Capabilities.Count: >= 1 and <= 5 }
        && contract.Capabilities.All(ValidCapability)
        && contract.Capabilities.Select(item => item.Kind).Distinct().Count() == contract.Capabilities.Count
        && (contract.Runtime is null || HasValidRuntimeShape(contract.Runtime));

    private static bool ValidCapability(ChallengeCapabilityContract? value)
    {
        if (value is null || !Enum.IsDefined(value.Kind)) return false;
        var count = (value.Break is not null ? 1 : 0) + (value.Fix is not null ? 1 : 0)
            + (value.Check is not null ? 1 : 0) + (value.Control is not null ? 1 : 0) + (value.Questionnaire is not null ? 1 : 0);
        return count == 1 && value.Kind switch
        {
            ChallengeCapabilityProtocol.Break => value.Break is { FlagStores: not null, EnvironmentTargets: not null } candidate
                && candidate.FlagStores.All(store => store is not null)
                && candidate.EnvironmentTargets.All(target => target is { ServiceName: not null, VariableName: not null })
                && candidate.FlagTemplate is not ({ Header: null } or { BodyTemplate: null }),
            ChallengeCapabilityProtocol.Fix => value.Fix is { TargetServiceName: not null, Entrypoint: not null, Command: not null },
            ChallengeCapabilityProtocol.Check => value.Check is { Checker: { Image: not null, Command: not null, Environment: not null }, Requirements: not null },
            ChallengeCapabilityProtocol.Control => value.Control is { UrlTemplate: not null },
            ChallengeCapabilityProtocol.Questionnaire => value.Questionnaire is { Questions: not null } questionnaire
                && questionnaire.Questions.All(question => question is { Title: not null, Body: not null, Options: not null, Classifications: not null, FileIds: not null }
                    && question.Options.All(option => option is { Content: not null })
                    && question.Classifications.All(item => item is { Name: not null })),
            _ => false
        };
    }

    public static bool HasValidRuntimeShape(ChallengeRuntimeContract? value)
    {
        if (value is null || !Enum.IsDefined(value.Kind) || value.UrlBindings is null
            || value.UrlBindings.Any(binding => binding is null || binding.UrlTemplate is null)) return false;
        return value.Kind switch
        {
            ChallengeRuntimeKindProtocol.Container => value.Ova is null && value.Limits is null
                && value.Container?.Services is { Count: >= 1 and <= 64 } services
                && services.All(service => service is { Name: not null, Image: not null, Command: not null, Arguments: not null, Environment: not null, InternalPorts: not null }),
            ChallengeRuntimeKindProtocol.Ova => value.Container is null && value.Limits is not null && value.Ova is { SourceUrl: not null, Sha256: not null },
            _ => false
        };
    }

    public static ChallengeDefinition ToDomain(Guid id, ChallengeDefinitionContract value)
    {
        if (!HasValidShape(value)) throw new ArgumentException("Capability kinds and branches must match exactly.", nameof(value));
        var definition = new ChallengeDefinition
        {
            ChallengeId = id, Runtime = value.Runtime is null ? null : ToDomain(id, value.Runtime),
            Capabilities = value.Capabilities.Select(ToDomain).ToList()
        };
        ChallengeDefinitionGraph.AssignChallengeId(definition, id);
        return definition;
    }

    public static ChallengeDefinitionContract FromDomain(ChallengeDefinition value) => new()
    {
        Runtime = value.Runtime is null ? null : FromDomain(value.Runtime),
        Capabilities = value.Capabilities.OrderBy(item => item.Kind).Select(FromDomain).ToArray()
    };

    private static ChallengeCapabilityDefinition ToDomain(ChallengeCapabilityContract value) => value.Kind switch
    {
        ChallengeCapabilityProtocol.Break => new BreakCapabilityDefinition
        {
            HasFlagTemplate = value.Break!.FlagTemplate is not null,
            FlagTemplate = value.Break.FlagTemplate is { } flag ? new() { Header = flag.Header, BodyTemplate = flag.BodyTemplate, LeetLiteralText = flag.LeetLiteralText } : new(),
            InjectionCommand = value.Break.Injection?.Command, InjectionTimeoutSeconds = value.Break.Injection?.TimeoutSeconds ?? 30,
            InjectionServiceName = value.Break.Injection?.ServiceName,
            EnvironmentTargets = value.Break.EnvironmentTargets.Select((target, position) => new FlagEnvironmentTarget
                { Position = position, ServiceName = target.ServiceName, VariableName = target.VariableName }).ToList(),
            FlagStores = value.Break.FlagStores.Select((store, position) => new FlagStoreDefinition
            {
                Id = store.Id, Position = position, Name = store.Name, DeliveryKind = (FlagDeliveryKind)store.Delivery,
                TargetServiceName = store.TargetServiceName, InjectionCommand = store.InjectionCommand,
                InjectionTimeoutSeconds = store.InjectionTimeoutSeconds, PreservesHistoricalFlags = store.PreservesHistoricalFlags
            }).ToList()
        },
        ChallengeCapabilityProtocol.Fix => new FixCapabilityDefinition
        {
            TargetServiceName = value.Fix!.TargetServiceName, Entrypoint = value.Fix.Entrypoint,
            TimeoutSeconds = value.Fix.TimeoutSeconds, MaximumUploadBytes = value.Fix.MaximumUploadBytes,
            Command = value.Fix.Command.Select((argument, position) => new FixCommandArgument { Position = position, Value = argument }).ToList()
        },
        ChallengeCapabilityProtocol.Check => new CheckCapabilityDefinition
        {
            Image = value.Check!.Checker.Image, TargetServiceName = value.Check.Checker.TargetServiceName ?? string.Empty,
            TimeoutSeconds = value.Check.Checker.TimeoutSeconds, ReadyTimeoutSeconds = value.Check.ReadyTimeoutSeconds,
            AcceptsFixInput = value.Check.AcceptsFixInput,
            Command = value.Check.Checker.Command.Select((argument, position) => new CheckCommandArgument { Position = position, Value = argument }).ToList(),
            Environment = value.Check.Checker.Environment.Select(variable => new CheckEnvironmentVariable { Name = variable.Key, Value = variable.Value }).ToList(),
            Requirements = value.Check.Requirements.Select(kind => new CheckRequirement { Kind = (CheckItemKind)kind }).ToList()
        },
        ChallengeCapabilityProtocol.Control => new ControlCapabilityDefinition
        {
            Protocol = (ControlProtocol)value.Control!.Protocol, UrlTemplate = value.Control.UrlTemplate,
            ServiceName = value.Control.ServiceName, ContainerPort = value.Control.ContainerPort,
            VmId = value.Control.VmId, GuestPort = value.Control.GuestPort, TimeoutSeconds = value.Control.TimeoutSeconds
        },
        ChallengeCapabilityProtocol.Questionnaire => Questionnaire(value.Questionnaire!),
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static QuestionnaireCapabilityDefinition Questionnaire(QuestionnaireCapabilityContract value) => new()
    {
        CurrentRevisionId = value.RevisionId,
        CurrentRevision = new()
        {
            Id = value.RevisionId, Number = value.RevisionNumber,
            Questions = value.Questions.Select((question, position) => new QuestionnaireQuestion
            {
                Id = question.Id, SourceId = question.SourceId, Position = position, Kind = (QuestionnaireQuestionKind)question.Kind,
                Difficulty = (QuestionnaireDifficulty)question.Difficulty, Title = question.Title, Body = question.Body, Explanation = question.Explanation,
                Options = question.Options.Select((option, order) => new QuestionnaireOption
                { Id = option.Id, Position = order, Content = option.Content, IsCorrect = option.IsCorrect, TruthValue = option.TruthValue }).ToList(),
                Classifications = question.Classifications.Select(item => new QuestionnaireClassification
                { Kind = (QuestionnaireClassificationKind)item.Kind, Name = item.Name.Trim(), NormalizedName = item.Name.Trim().ToUpperInvariant() }).ToList(),
                Files = question.FileIds.Select(id => new QuestionnaireFile { FileId = id }).ToList()
            }).ToList()
        }
    };

    private static ChallengeCapabilityContract FromDomain(ChallengeCapabilityDefinition value) => value switch
    {
        BreakCapabilityDefinition capability => new()
        {
            Kind = ChallengeCapabilityProtocol.Break, Break = new()
            {
                FlagTemplate = capability.HasFlagTemplate ? new(capability.FlagTemplate.Header, capability.FlagTemplate.BodyTemplate, capability.FlagTemplate.LeetLiteralText) : null,
                Injection = capability.InjectionCommand is null ? null : new(capability.InjectionCommand, capability.InjectionTimeoutSeconds, capability.InjectionServiceName),
                EnvironmentTargets = capability.EnvironmentTargets.OrderBy(target => target.Position)
                    .Select(target => new FlagEnvironmentTargetContract(target.ServiceName, target.VariableName)).ToArray(),
                FlagStores = capability.FlagStores.OrderBy(item => item.Position).Select(store => new FlagStoreContract(store.Id, store.Name,
                    (FlagDeliveryProtocol)store.DeliveryKind, store.TargetServiceName, store.InjectionCommand, store.InjectionTimeoutSeconds, store.PreservesHistoricalFlags)).ToArray()
            }
        },
        FixCapabilityDefinition capability => new()
        {
            Kind = ChallengeCapabilityProtocol.Fix, Fix = new() { TargetServiceName = capability.TargetServiceName,
                Entrypoint = capability.Entrypoint, TimeoutSeconds = capability.TimeoutSeconds, MaximumUploadBytes = capability.MaximumUploadBytes,
                Command = capability.Command.OrderBy(item => item.Position).Select(item => item.Value).ToArray() }
        },
        CheckCapabilityDefinition capability => new()
        {
            Kind = ChallengeCapabilityProtocol.Check, Check = new()
            {
                Checker = new(capability.Image, capability.Command.OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                    capability.Environment.ToDictionary(item => item.Name, item => item.Value), capability.TimeoutSeconds, capability.TargetServiceName),
                ReadyTimeoutSeconds = capability.ReadyTimeoutSeconds, AcceptsFixInput = capability.AcceptsFixInput,
                Requirements = capability.Requirements.Select(item => (CheckItemProtocol)item.Kind).ToArray()
            }
        },
        ControlCapabilityDefinition capability => new()
        {
            Kind = ChallengeCapabilityProtocol.Control,
            Control = new((ControlProtocolKind)capability.Protocol, capability.UrlTemplate, capability.ServiceName, capability.ContainerPort,
                capability.VmId, capability.GuestPort, capability.TimeoutSeconds)
        },
        QuestionnaireCapabilityDefinition capability => new()
        {
            Kind = ChallengeCapabilityProtocol.Questionnaire,
            Questionnaire = new(capability.CurrentRevisionId, capability.CurrentRevision.Number,
                capability.CurrentRevision.Questions.OrderBy(item => item.Position).Select(question => new QuestionnaireQuestionContract(
                    question.Id, question.SourceId, (QuestionnaireQuestionProtocol)question.Kind, (QuestionnaireDifficultyProtocol)question.Difficulty,
                    question.Title, question.Body, question.Explanation,
                    question.Options.OrderBy(item => item.Position).Select(option => new QuestionnaireOptionContract(option.Id, option.Content, option.IsCorrect, option.TruthValue)).ToArray(),
                    question.Classifications.Select(item => new QuestionnaireClassificationContract((QuestionnaireClassificationProtocol)item.Kind, item.Name)).ToArray(),
                    question.Files.Select(item => item.FileId).ToArray())).ToArray())
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static ChallengeRuntimeContract FromDomain(ChallengeRuntimeTemplateEntity value) => new()
    {
        Kind = value is ContainerChallengeRuntimeTemplate ? ChallengeRuntimeKindProtocol.Container : ChallengeRuntimeKindProtocol.Ova,
        OperationTimeoutSeconds = value.OperationTimeoutSeconds,
        EgressPolicy = (RuntimeEgressPolicyProtocol)value.EgressPolicy,
        Limits = value is OvaChallengeRuntimeTemplate resources ? new(resources.Limits.MemoryBytes, resources.Limits.CpuMillicores, resources.Limits.PidsLimit) : null,
        Container = value is ContainerChallengeRuntimeTemplate container ? new()
        {
            Services = container.Services.OrderBy(item => item.Position).Select(service => new RuntimeServiceContract
            {
                Name = service.Name, Image = service.Image, CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                Command = service.Commands.Where(item => !item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                Arguments = service.Commands.Where(item => item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                Environment = service.Environment.ToDictionary(item => item.Name, item => item.Value),
                InternalPorts = service.InternalPorts.Select(item => item.Port).ToArray()
            }).ToArray()
        } : null,
        Ova = value is OvaChallengeRuntimeTemplate ova ? new() { SourceUrl = ova.OvaSourceUrl, Sha256 = ova.Sha256 } : null,
        UrlBindings = value.UrlBindings.OrderBy(item => item.Position).Select(item => new RuntimeUrlBindingContract(item.UrlTemplate,
            (RuntimeExposureProtocol)item.Exposure, item.ContainerPort, item.ServiceName, item.VmId, item.GuestPort)).ToArray()
    };

    private static ChallengeRuntimeTemplateEntity ToDomain(Guid id, ChallengeRuntimeContract value)
    {
        if (!HasValidRuntimeShape(value)) throw new ArgumentException("Invalid Runtime definition.", nameof(value));
        ChallengeRuntimeTemplateEntity result = value.Kind switch
        {
            ChallengeRuntimeKindProtocol.Container => new ContainerChallengeRuntimeTemplate
            {
                Services = value.Container!.Services.Select((service, position) => new ChallengeRuntimeService
                {
                    Name = service.Name, Position = position, Image = service.Image, CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                    Commands = service.Command.Select((argument, order) => new ChallengeRuntimeServiceCommand { Position = order, Value = argument })
                        .Concat(service.Arguments.Select((argument, order) => new ChallengeRuntimeServiceCommand { IsArgument = true, Position = order, Value = argument })).ToList(),
                    Environment = service.Environment.Select(item => new ChallengeRuntimeServiceEnvironment { Name = item.Key, Value = item.Value }).ToList(),
                    InternalPorts = service.InternalPorts.Select(port => new ChallengeRuntimeServicePort { Port = port }).ToList()
                }).ToList()
            },
            ChallengeRuntimeKindProtocol.Ova => new OvaChallengeRuntimeTemplate { OvaSourceUrl = value.Ova!.SourceUrl, Sha256 = value.Ova.Sha256,
                HasExplicitLimits = true, Limits = new() { MemoryBytes = value.Limits!.MemoryBytes, CpuMillicores = value.Limits.CpuMillicores, PidsLimit = value.Limits.PidsLimit } },
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        result.ChallengeId = id; result.OperationTimeoutSeconds = value.OperationTimeoutSeconds;
        result.EgressPolicy = (PersistedRuntimeEgressPolicy)value.EgressPolicy;
        result.UrlBindings = value.UrlBindings.Select((binding, position) => new ChallengeRuntimeUrlBinding
        {
            ChallengeId = id, Position = position, UrlTemplate = binding.UrlTemplate,
            Exposure = (PersistedRuntimeExposure)binding.Exposure, ContainerPort = binding.ContainerPort,
            ServiceName = binding.ServiceName, VmId = binding.VmId, GuestPort = binding.GuestPort
        }).ToList();
        return result;
    }
}

public sealed class CreateChallengeTemplateEndpoint(CreateChallengeTemplate create, IUserContext user, TimeProvider timeProvider)
    : Endpoint<CreateChallengeTemplateRequest, Results<Created<ChallengeTemplateResponse>, Conflict<ChallengeTemplateConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges"); AuthSchemes("Bearer"); Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankCreateTemplate"));
    }
    public override async Task<Results<Created<ChallengeTemplateResponse>, Conflict<ChallengeTemplateConflictResponse>, ProblemHttpResult>>
        ExecuteAsync(CreateChallengeTemplateRequest request, CancellationToken ct)
    {
        var result = await create.ExecuteAsync(ChallengeTemplateMapper.ToCommand(request, user.UserId, timeProvider.GetUtcNow()), ct);
        return result.State switch
        {
            ChallengeTemplateWriteState.Succeeded => TypedResults.Created($"/api/v1/admin/challenges/{result.Template!.Id}", ChallengeTemplateMapper.ToResponse(result.Template)),
            ChallengeTemplateWriteState.InvalidRequest or ChallengeTemplateWriteState.InvalidDefinition => ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: ApiMessages.Get(ApiMessageId.CreateChallengeTemplateTitleChallengeTemplateWasCreated),
                detail: ApiMessages.Get(ApiMessageId.CreateChallengeTemplateTitleChallengeTemplateWasCreated)),
            ChallengeTemplateWriteState.ResourceIdConflict or ChallengeTemplateWriteState.UserNotFound or ChallengeTemplateWriteState.RoleNotEligible =>
                TypedResults.Conflict(ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException($"Unsupported template creation state {result.State}.")
        };
    }
}

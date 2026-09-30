using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeVisibilityProtocol>))]
public enum ChallengeVisibilityProtocol
{
    Private,
    Shared
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<SpecificationKindProtocol>))]
public enum SpecificationKindProtocol
{
    Attachment,
    AwdRound,
    RuntimeDefinition,
    Hint,
    RuntimeInstance
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeAllocationProtocol>))]
public enum RuntimeAllocationProtocol { Shared, PerTeam }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeFlagSourceProtocol>))]
public enum RuntimeFlagSourceProtocol { Static, PerTeam, AwdRotation }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeEgressPolicyProtocol>))]
public enum RuntimeEgressPolicyProtocol { Isolated, InternetOnly }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeExposureProtocol>))]
public enum RuntimeExposureProtocol { OwnerOnly, Participants }

public sealed record RuntimeResourceLimitsContract(long MemoryBytes, long CpuMillicores, long PidsLimit);
public sealed record RuntimeUrlBindingContract(
    string UrlTemplate,
    RuntimeExposureProtocol Exposure,
    int? ContainerPort,
    string? ServiceName,
    string? VmId,
    int? GuestPort,
    bool IsControlCheck);
[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeRuntimeKindProtocol>))]
public enum ChallengeRuntimeKindProtocol { Container, Ova }

public sealed class ChallengeRuntimeContract
{
    public required ChallengeRuntimeKindProtocol Kind { get; set; }
    public RuntimeAllocationProtocol Allocation { get; set; }
    public RuntimeResourceLimitsContract? Limits { get; set; }
    public int? TtlSeconds { get; set; }
    public int? OperationTimeoutSeconds { get; set; }
    public RuntimeFlagSourceProtocol FlagSource { get; set; }
    public RuntimeEgressPolicyProtocol EgressPolicy { get; set; }
    public IReadOnlyList<RuntimeUrlBindingContract> UrlBindings { get; set; } = [];
    public ContainerChallengeRuntimeContract? Container { get; set; }
    public OvaChallengeRuntimeContract? Ova { get; set; }
}

public sealed class ContainerChallengeRuntimeContract
{
    public required IReadOnlyList<RuntimeServiceContract> Services { get; set; }
}

public sealed class RuntimeServiceContract
{
    public required string Name { get; set; }
    public required string Image { get; set; }
    public decimal CpuCores { get; set; } = 0.5m;
    public long MemoryMiB { get; set; } = 512;
    public IReadOnlyList<string> Command { get; set; } = [];
    public IReadOnlyList<string> Arguments { get; set; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    public string? FlagEnvironmentVariableName { get; set; }
    public IReadOnlyList<int> InternalPorts { get; set; } = [];
}

public sealed class OvaChallengeRuntimeContract
{
    public required string SourceUrl { get; set; }
    public required string Sha256 { get; set; }
}

public sealed record RunnerJobContract(
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Environment,
    int TimeoutSeconds,
    string? TargetServiceName);
public sealed record FlagInjectionContract(string Command, int TimeoutSeconds, string? ServiceName);

public sealed class ChallengeDefinitionContract
{
    public required GameModeProtocol Mode { get; set; }
    public FlagTemplateContract? FlagTemplate { get; set; }
    public ChallengeRuntimeContract? Runtime { get; set; }
    public RunnerJobContract? Checker { get; set; }
    public string? PatchEntrypoint { get; set; }
    public IReadOnlyList<string> PatchCommand { get; set; } = [];
    public int? PatchTimeoutSeconds { get; set; }
    public int? ReadyTimeoutSeconds { get; set; }
    public long? MaximumPatchUploadBytes { get; set; }
    public bool CheckerFixInput { get; set; }
    public CtfChallengeDefinitionContract? Ctf { get; set; }
    public AwdChallengeDefinitionContract? Awd { get; set; }
    public AwdpChallengeDefinitionContract? Awdp { get; set; }
    public KohChallengeDefinitionContract? Koh { get; set; }
}

public sealed class CtfChallengeDefinitionContract
{
    public required NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol InteractionKind { get; set; }
}
public sealed class AwdChallengeDefinitionContract
{
    public required FlagInjectionContract? FlagInjection { get; set; }
}
public sealed class AwdpChallengeDefinitionContract;
public sealed class KohChallengeDefinitionContract;

public sealed class CreateChallengeTemplateRequest
{
    public Guid? Id { get; set; }
    public GameModeProtocol Mode { get; set; }
    public ChallengeVisibilityProtocol Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public required ChallengeDefinitionContract Definition { get; set; }
}

public sealed record ChallengeTemplateResponse(
    Guid Id,
    Guid OwnerId,
    IReadOnlyList<Guid> ManagerIds,
    GameModeProtocol Mode,
    ChallengeVisibilityProtocol Visibility,
    string Title,
    string? Description,
    string Direction,
    ChallengeDefinitionContract Definition,
    DateTimeOffset? DeletedAt,
    int ActiveCompetitionReferenceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol InteractionKind);

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ChallengeTemplateConflictCode>))]
public enum ChallengeTemplateConflictCode
{
    ResourceIdConflict,
    ActiveCompetitionModeConflict,
    ActiveRuntimeDefinitionConflict,
    OwnerIncludedInManagerSet,
    UserNotFound,
    RoleNotEligible,
    ExperimentalFeatureDisabled,
    InteractionKindConflict
}

public sealed record ChallengeTemplateConflictResponse(
    [property: Required, JsonRequired] ChallengeTemplateConflictCode Code,
    [property: Required, JsonRequired] string Detail,
    [property: Required, JsonRequired] IReadOnlyList<Guid> UserIds);

internal static class ChallengeTemplateWriteResponseMapper
{
    public static ChallengeTemplateConflictResponse ToConflict(
        ChallengeTemplateWriteResult result) =>
        new(
            result.State switch
            {
                ChallengeTemplateWriteState.ResourceIdConflict =>
                    ChallengeTemplateConflictCode.ResourceIdConflict,
                ChallengeTemplateWriteState.ActiveCompetitionModeConflict =>
                    ChallengeTemplateConflictCode.ActiveCompetitionModeConflict,
                ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict =>
                    ChallengeTemplateConflictCode.ActiveRuntimeDefinitionConflict,
                ChallengeTemplateWriteState.OwnerIncludedInManagerSet =>
                    ChallengeTemplateConflictCode.OwnerIncludedInManagerSet,
                ChallengeTemplateWriteState.UserNotFound =>
                    ChallengeTemplateConflictCode.UserNotFound,
                ChallengeTemplateWriteState.RoleNotEligible =>
                    ChallengeTemplateConflictCode.RoleNotEligible,
                ChallengeTemplateWriteState.ExperimentalFeatureDisabled =>
                    ChallengeTemplateConflictCode.ExperimentalFeatureDisabled,
                ChallengeTemplateWriteState.InteractionKindConflict =>
                    ChallengeTemplateConflictCode.InteractionKindConflict,
                _ => throw new InvalidOperationException(
                    $"Unsupported challenge template conflict state: {result.State}.")
            },
            result.Detail ?? (result.State switch
            {
                ChallengeTemplateWriteState.ResourceIdConflict =>
                    "The requested challenge template identifier is already in use.",
                ChallengeTemplateWriteState.ActiveCompetitionModeConflict =>
                    "The template mode cannot change while active competitions reference it.",
                ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict =>
                    "The template definition cannot change while active Runtimes use it.",
                ChallengeTemplateWriteState.OwnerIncludedInManagerSet =>
                    "The template owner cannot also be listed as a manager.",
                ChallengeTemplateWriteState.UserNotFound =>
                    "One or more selected template managers no longer exist.",
                ChallengeTemplateWriteState.RoleNotEligible =>
                    "One or more selected accounts do not have a platform role eligible to manage challenge templates.",
                ChallengeTemplateWriteState.ExperimentalFeatureDisabled =>
                    "CTF PatchVerification is disabled in platform settings.",
                ChallengeTemplateWriteState.InteractionKindConflict =>
                    "The CTF interaction kind can change only before the template is referenced or used.",
                _ => "The challenge template conflicts with its current state."
            }),
            result.UserIds ?? []);
}

public sealed class CreateChallengeTemplateValidator : Validator<CreateChallengeTemplateRequest>
{
    public CreateChallengeTemplateValidator()
    {
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
        RuleFor(request => request.Definition)
            .Must((request, definition) =>
                ChallengeDefinitionContractMapper.HasValidShape(definition)
                && definition!.Mode == request.Mode)
            .WithMessage("Definition must contain exactly the branch matching the challenge mode.");
    }
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
internal static partial class ChallengeTemplateMapper
{
    public static CreateChallengeTemplateCommand ToCommand(
        CreateChallengeTemplateRequest request,
        Guid ownerId,
        DateTimeOffset createdAt) =>
        new(
            request.Id,
            ownerId,
            CompetitionProtocolMapper.ToDomain(request.Mode),
            ToDomain(request.Visibility),
            request.Title,
            request.Description,
            request.Direction,
            ChallengeDefinitionContractMapper.ToDomain(
                request.Id ?? Guid.Empty,
                CompetitionProtocolMapper.ToDomain(request.Mode),
                request.Definition),
            createdAt);
    public static ChallengeTemplateResponse ToResponse(ChallengeTemplateView source) => new(
        source.Id,
        source.OwnerId,
        source.ManagerIds,
        ToProtocol(source.Mode),
        ToProtocol(source.Visibility),
        source.Title,
        source.Description,
        source.Direction,
        ChallengeDefinitionContractMapper.FromDomain(source.Definition),
        source.DeletedAt,
        source.ActiveCompetitionReferenceCount,
        source.CreatedAt,
        source.UpdatedAt,
        ToProtocol(source.InteractionKind));

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ChallengeVisibility ToDomain(ChallengeVisibilityProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial ChallengeVisibilityProtocol ToProtocol(ChallengeVisibility value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SpecificationKind ToDomain(SpecificationKindProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial SpecificationKindProtocol ToProtocol(SpecificationKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial GameModeProtocol ToProtocol(GameMode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol ToProtocol(
        CtfInteractionKind value);
}

public static class ChallengeDefinitionContractMapper
{
    public static bool HasValidShape(ChallengeDefinitionContract? contract)
    {
        if (contract is null || !Enum.IsDefined(contract.Mode)
            || contract.PatchCommand is null
            || contract.Checker is { Command: null } or { Environment: null } or { Image: null }
            || contract.FlagTemplate is { Header: null } or { BodyTemplate: null })
            return false;
        var count = (contract.Ctf is not null ? 1 : 0)
            + (contract.Awd is not null ? 1 : 0)
            + (contract.Awdp is not null ? 1 : 0)
            + (contract.Koh is not null ? 1 : 0);
        return count == 1
            && (contract.Mode switch
            {
                GameModeProtocol.Ctf => contract.Ctf is not null,
                GameModeProtocol.Awd => contract.Awd is not null,
                GameModeProtocol.Awdp => contract.Awdp is not null,
                GameModeProtocol.Koh => contract.Koh is not null,
                _ => false
            })
            && (contract.Runtime is null || HasValidRuntimeShape(contract.Runtime));
    }

    public static bool HasValidRuntimeShape(ChallengeRuntimeContract? contract)
    {
        if (contract is null || !Enum.IsDefined(contract.Kind) || contract.UrlBindings is null
            || contract.UrlBindings.Any(binding => binding is null || binding.UrlTemplate is null)) return false;
        return contract.Kind switch
        {
            ChallengeRuntimeKindProtocol.Container => contract.Ova is null && contract.Limits is null
                && contract.Container?.Services is { } services && services.All(service => service is
                    { Name: not null, Image: not null, Command: not null, Arguments: not null, Environment: not null, InternalPorts: not null }),
            ChallengeRuntimeKindProtocol.Ova => contract.Container is null && contract.Limits is not null
                && contract.Ova is { SourceUrl: not null, Sha256: not null },
            _ => false
        };
    }

    public static ChallengeDefinition ToDomain(
        Guid challengeId,
        GameMode expectedMode,
        ChallengeDefinitionContract contract)
    {
        if (!HasValidShape(contract))
            throw new ArgumentException("Definition mode and branch must match exactly.", nameof(contract));
        var definition = CreateDomain(challengeId, contract);
        if (definition.Mode != expectedMode)
            throw new InvalidOperationException(
                $"Definition mode {definition.Mode} does not match challenge mode {expectedMode}.");
        return definition;
    }

    public static ChallengeDefinitionContract FromDomain(ChallengeDefinition value)
    {
        ChallengeDefinitionContract result = value switch
        {
            CtfChallengeDefinition ctf => new ChallengeDefinitionContract
            {
                Mode = GameModeProtocol.Ctf,
                Ctf = new CtfChallengeDefinitionContract
                {
                    InteractionKind = (NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol)ctf.InteractionKind
                }
            },
            AwdChallengeDefinition awd => new ChallengeDefinitionContract
            {
                Mode = GameModeProtocol.Awd,
                Awd = new AwdChallengeDefinitionContract
                {
                    FlagInjection = awd.FlagInjectionCommand is null
                        ? null
                        : new FlagInjectionContract(
                            awd.FlagInjectionCommand,
                            awd.FlagInjectionTimeoutSeconds ?? 30,
                            awd.FlagInjectionServiceName)
                }
            },
            AwdpChallengeDefinition => new ChallengeDefinitionContract
            {
                Mode = GameModeProtocol.Awdp,
                Awdp = new AwdpChallengeDefinitionContract()
            },
            KohChallengeDefinition => new ChallengeDefinitionContract
            {
                Mode = GameModeProtocol.Koh,
                Koh = new KohChallengeDefinitionContract()
            },
            _ => throw new InvalidOperationException(
                $"Unsupported challenge definition {value.GetType().Name}.")
        };
        result.Runtime = value.Runtime is null ? null : FromDomain(value.Runtime);
        result.Checker = value.Checker is null
            ? null
            : new RunnerJobContract(
                value.Checker.Image,
                Strings(value, ChallengeDefinitionStringKind.CheckerCommand),
                KeyValues(value, ChallengeDefinitionStringKind.CheckerEnvironment),
                value.Checker.TimeoutSeconds,
                value.Checker.TargetServiceName);
        result.PatchEntrypoint = value.PatchEntrypoint;
        result.PatchCommand = Strings(value, ChallengeDefinitionStringKind.PatchCommand);
        result.PatchTimeoutSeconds = value.PatchTimeoutSeconds;
        result.ReadyTimeoutSeconds = value.ReadyTimeoutSeconds;
        result.MaximumPatchUploadBytes = value.MaximumPatchUploadBytes;
        result.CheckerFixInput = value.CheckerFixInput;
        result.FlagTemplate = value.HasFlagTemplate
            ? new FlagTemplateContract(
                value.FlagTemplate.Header,
                value.FlagTemplate.BodyTemplate,
                value.FlagTemplate.LeetLiteralText)
            : null;
        return result;
    }

    private static ChallengeDefinition CreateDomain(Guid challengeId, ChallengeDefinitionContract value)
    {
        ChallengeDefinition result = value.Mode switch
        {
            GameModeProtocol.Ctf => new CtfChallengeDefinition
            {
                InteractionKind = (CtfInteractionKind)value.Ctf!.InteractionKind
            },
            GameModeProtocol.Awd => new AwdChallengeDefinition
            {
                FlagInjectionCommand = value.Awd!.FlagInjection?.Command,
                FlagInjectionTimeoutSeconds = value.Awd.FlagInjection?.TimeoutSeconds,
                FlagInjectionServiceName = value.Awd.FlagInjection?.ServiceName
            },
            GameModeProtocol.Awdp => new AwdpChallengeDefinition(),
            GameModeProtocol.Koh => new KohChallengeDefinition(),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge definition contract {value.GetType().Name}.")
        };
        result.ChallengeId = challengeId;
        result.Runtime = value.Runtime is null ? null : ToDomain(challengeId, value.Runtime);
        result.PatchEntrypoint = value.PatchEntrypoint;
        result.PatchTimeoutSeconds = value.PatchTimeoutSeconds;
        result.ReadyTimeoutSeconds = value.ReadyTimeoutSeconds;
        result.MaximumPatchUploadBytes = value.MaximumPatchUploadBytes;
        result.CheckerFixInput = value.CheckerFixInput;
        result.HasFlagTemplate = value.FlagTemplate is not null;
        if (value.FlagTemplate is not null)
        {
            result.FlagTemplate = new FlagTemplateValue
            {
                Header = value.FlagTemplate.Header,
                BodyTemplate = value.FlagTemplate.BodyTemplate,
                LeetLiteralText = value.FlagTemplate.LeetLiteralText
            };
        }
        result.Checker = value.Checker is null
            ? null
            : new ChallengeCheckerDefinition
            {
                ChallengeId = challengeId,
                Image = value.Checker.Image,
                TimeoutSeconds = value.Checker.TimeoutSeconds,
                TargetServiceName = value.Checker.TargetServiceName
            };
        result.StringItems = value.PatchCommand.Select((item, position) =>
                new ChallengeDefinitionStringItem
                {
                    ChallengeId = challengeId,
                    Kind = ChallengeDefinitionStringKind.PatchCommand,
                    Position = position,
                    Value = item
                })
            .Concat((value.Checker?.Command ?? []).Select((item, position) =>
                new ChallengeDefinitionStringItem
                {
                    ChallengeId = challengeId,
                    Kind = ChallengeDefinitionStringKind.CheckerCommand,
                    Position = position,
                    Value = item
                }))
            .Concat((value.Checker?.Environment ?? new Dictionary<string, string>())
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select((item, position) => new ChallengeDefinitionStringItem
                {
                    ChallengeId = challengeId,
                    Kind = ChallengeDefinitionStringKind.CheckerEnvironment,
                    Position = position,
                    Key = item.Key,
                    Value = item.Value
                }))
            .ToList();
        return result;
    }

    private static ChallengeRuntimeContract FromDomain(ChallengeRuntimeTemplateEntity value)
    {
        ChallengeRuntimeContract result = value switch
        {
            ContainerChallengeRuntimeTemplate container => new ChallengeRuntimeContract
            {
                Kind = ChallengeRuntimeKindProtocol.Container,
                Container = new ContainerChallengeRuntimeContract
                {
                    Services = container.Services.OrderBy(service => service.Position).Select(service => new RuntimeServiceContract
                    {
                        Name = service.Name, Image = service.Image, CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                        Command = service.Commands.Where(item => !item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                        Arguments = service.Commands.Where(item => item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                        Environment = service.Environment.ToDictionary(item => item.Name, item => item.Value),
                        InternalPorts = service.InternalPorts.Select(item => item.Port).ToArray(),
                        FlagEnvironmentVariableName = service.FlagEnvironmentVariableName
                    }).ToArray()
                }
            },
            OvaChallengeRuntimeTemplate ova => new ChallengeRuntimeContract
            {
                Kind = ChallengeRuntimeKindProtocol.Ova,
                Ova = new OvaChallengeRuntimeContract
                {
                    SourceUrl = ova.OvaSourceUrl,
                    Sha256 = ova.Sha256
                }
            },
            _ => throw new InvalidOperationException(
                $"Unsupported challenge Runtime {value.GetType().Name}.")
        };
        result.Allocation = (RuntimeAllocationProtocol)value.Allocation;
        result.Limits = value is OvaChallengeRuntimeTemplate resources ? new RuntimeResourceLimitsContract(
            resources.Limits.MemoryBytes, resources.Limits.CpuMillicores, resources.Limits.PidsLimit) : null;
        result.TtlSeconds = value.TtlSeconds;
        result.OperationTimeoutSeconds = value.OperationTimeoutSeconds;
        result.FlagSource = (RuntimeFlagSourceProtocol)value.FlagSource;
        result.EgressPolicy = (RuntimeEgressPolicyProtocol)value.EgressPolicy;
        result.UrlBindings = value.UrlBindings.OrderBy(item => item.Position).Select(item =>
            new RuntimeUrlBindingContract(
                item.UrlTemplate,
                (RuntimeExposureProtocol)item.Exposure,
                item.ContainerPort,
                item.ServiceName,
                item.VmId,
                item.GuestPort,
                item.IsControlCheck)).ToArray();
        return result;
    }

    private static ChallengeRuntimeTemplateEntity ToDomain(
        Guid challengeId,
        ChallengeRuntimeContract value)
    {
        if (!HasValidRuntimeShape(value))
            throw new ArgumentException("Runtime kind and branch must match exactly.", nameof(value));
        ChallengeRuntimeTemplateEntity result = value.Kind switch
        {
            ChallengeRuntimeKindProtocol.Container => new ContainerChallengeRuntimeTemplate
            {
                Services = value.Container!.Services.Select((service, position) => new ChallengeRuntimeService
                {
                    ChallengeId = challengeId, Name = service.Name, Position = position, Image = service.Image,
                    CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                    FlagEnvironmentVariableName = service.FlagEnvironmentVariableName,
                    Commands = service.Command.Select((item, index) => new ChallengeRuntimeServiceCommand
                    { ChallengeId = challengeId, ServiceName = service.Name, Position = index, Value = item })
                        .Concat(service.Arguments.Select((item, index) => new ChallengeRuntimeServiceCommand
                        { ChallengeId = challengeId, ServiceName = service.Name, IsArgument = true, Position = index, Value = item })).ToList(),
                    Environment = service.Environment.Select(item => new ChallengeRuntimeServiceEnvironment
                    { ChallengeId = challengeId, ServiceName = service.Name, Name = item.Key, Value = item.Value }).ToList(),
                    InternalPorts = service.InternalPorts.Select(port => new ChallengeRuntimeServicePort
                    { ChallengeId = challengeId, ServiceName = service.Name, Port = port }).ToList()
                }).ToList()
            },
            ChallengeRuntimeKindProtocol.Ova => new OvaChallengeRuntimeTemplate
            {
                OvaSourceUrl = value.Ova!.SourceUrl,
                Sha256 = value.Ova.Sha256
            },
            _ => throw new InvalidOperationException(
                $"Unsupported challenge Runtime contract {value.GetType().Name}.")
        };
        result.ChallengeId = challengeId;
        result.Allocation = (PersistedRuntimeAllocation)value.Allocation;
        if (result is OvaChallengeRuntimeTemplate ova)
        {
            ova.Limits = new() { MemoryBytes = value.Limits!.MemoryBytes, CpuMillicores = value.Limits.CpuMillicores, PidsLimit = value.Limits.PidsLimit };
            ova.HasExplicitLimits = true;
        }
        result.TtlSeconds = value.TtlSeconds;
        result.OperationTimeoutSeconds = value.OperationTimeoutSeconds;
        result.FlagSource = (PersistedRuntimeFlagSource)value.FlagSource;
        result.EgressPolicy = (PersistedRuntimeEgressPolicy)value.EgressPolicy;
        result.UrlBindings = value.UrlBindings.Select((item, position) =>
            new ChallengeRuntimeUrlBinding
            {
                ChallengeId = challengeId,
                Position = position,
                IsControlCheck = item.IsControlCheck,
                UrlTemplate = item.UrlTemplate,
                Exposure = (PersistedRuntimeExposure)item.Exposure,
                ContainerPort = item.ContainerPort,
                ServiceName = item.ServiceName,
                VmId = item.VmId,
                GuestPort = item.GuestPort
            }).ToList();
        return result;
    }

    private static string[] Strings(ChallengeDefinition value, ChallengeDefinitionStringKind kind) =>
        value.StringItems.Where(item => item.Kind == kind).OrderBy(item => item.Position)
            .Select(item => item.Value).ToArray();
    private static IReadOnlyDictionary<string, string> KeyValues(
        ChallengeDefinition value,
        ChallengeDefinitionStringKind kind) =>
        value.StringItems.Where(item => item.Kind == kind)
            .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal);

}

public sealed class CreateChallengeTemplateEndpoint(
    CreateChallengeTemplate create,
    IUserContext user,
    TimeProvider timeProvider,
    ILogger<CreateChallengeTemplateEndpoint> logger)
    : Endpoint<
        CreateChallengeTemplateRequest,
        Results<
            Created<ChallengeTemplateResponse>,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankCreateTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Creates a global challenge template.";
            summary.Description = "Creates a reusable question-bank template independent of any competition.";
        });
    }

    public override async Task<
        Results<
            Created<ChallengeTemplateResponse>,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        CreateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var result = await create.ExecuteAsync(
            ChallengeTemplateMapper.ToCommand(request, user.UserId, timeProvider.GetUtcNow()),
            ct);
        if (result.State is ChallengeTemplateWriteState.InvalidRequest
            or ChallengeTemplateWriteState.InvalidDefinition)
        {
            logger.LogWarning(
                "Challenge template creation rejected for mode {Mode}: {State}. {Detail}",
                request.Mode,
                result.State,
                result.Detail);
        }
        return result.State switch
        {
            ChallengeTemplateWriteState.Succeeded =>
                TypedResults.Created(
                    $"/api/v1/admin/challenges/{result.Template!.Id}",
                    ChallengeTemplateMapper.ToResponse(result.Template)),
            ChallengeTemplateWriteState.InvalidRequest
                or ChallengeTemplateWriteState.InvalidDefinition =>
                TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Challenge template was not created.",
                detail: result.Detail),
            ChallengeTemplateWriteState.ResourceIdConflict
                or ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible
                or ChallengeTemplateWriteState.ExperimentalFeatureDisabled =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template creation state: {result.State}.")
        };
    }
}

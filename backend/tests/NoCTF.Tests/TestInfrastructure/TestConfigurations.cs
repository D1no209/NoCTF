using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Endpoints.Administration.Competitions;

namespace NoCTF.Tests;

internal static class TestConfigurations
{
    public static CompetitionModeConfiguration Competition(GameMode mode) =>
        CompetitionModeConfigurationDefaults.Create(mode, Guid.Empty);

    public static CompetitionChallengeRules Rules(GameMode mode)
        => new GameModeChallengeConfigurationCatalog().CreateDefaultRules(mode, Guid.Empty);

    public static ChallengeDefinition Definition(GameMode mode)
        => new GameModeChallengeConfigurationCatalog().CreateDefaultDefinition(mode, Guid.Empty);

    public static CompetitionModeConfiguration Clone(
        CompetitionModeConfiguration source,
        Guid competitionId = default) =>
        CompetitionModeConfigurationContractMapper.ToDomain(
            competitionId,
            source.Mode,
            CompetitionModeConfigurationContractMapper.FromDomain(source));

    public static CompetitionChallengeRules Clone(
        CompetitionChallengeRules source,
        Guid competitionChallengeId = default) =>
        CompetitionChallengeRulesContractMapper.ToDomain(
            competitionChallengeId,
            source.Mode,
            CompetitionChallengeRulesContractMapper.FromDomain(source));

    public static ChallengeDefinition Clone(
        ChallengeDefinition source,
        Guid challengeId = default) =>
        ChallengeDefinitionContractMapper.ToDomain(
            challengeId,
            source.Mode,
            ChallengeDefinitionContractMapper.FromDomain(source));

    public static CompetitionModeConfiguration Competition(GameMode mode, string json) => mode switch
    {
        GameMode.Ctf => Competition(Parse<CtfConfiguration>(json, 2)),
        GameMode.Awd => Competition(Parse<AwdConfiguration>(json, 2)),
        GameMode.Awdp => Competition(Parse<AwdpConfiguration>(json, 4)),
        GameMode.Koh => Competition(Parse<KohConfiguration>(json, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static CompetitionChallengeRules Rules(GameMode mode, string json) => mode switch
    {
        GameMode.Ctf => Rules(Parse<CtfChallengeConfiguration>(json, 2)),
        GameMode.Awd => Rules(Parse<AwdChallengeConfiguration>(json, 4)),
        GameMode.Awdp => Rules(Parse<AwdpChallengeConfiguration>(json, 4)),
        GameMode.Koh => Rules(Parse<KohChallengeConfiguration>(json, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static ChallengeDefinition Definition(GameMode mode, string json) => mode switch
    {
        GameMode.Ctf => Definition(Parse<CtfChallengeConfiguration>(json, 3)),
        GameMode.Awd => Definition(Parse<AwdChallengeConfiguration>(json, 4)),
        GameMode.Awdp => Definition(Parse<AwdpChallengeConfiguration>(json, 4)),
        GameMode.Koh => Definition(Parse<KohChallengeConfiguration>(json, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static CompetitionModeConfiguration Competition(CtfConfiguration value) =>
        Common(new CtfCompetitionModeConfiguration
        {
            DefaultScoreCurve = Curve(value.DefaultScoreCurve),
            BloodRewards = value.BloodRewards.Select((reward, position) =>
                new CompetitionBloodReward
                {
                    Position = position,
                    Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                    Value = reward.Value
                }).ToList(),
            WrongSubmissionPenalty = value.WrongSubmissionPenalty
        }, value.FlagTemplate);

    private static CompetitionModeConfiguration Competition(AwdConfiguration value) =>
        Common(new AwdCompetitionModeConfiguration
        {
            HardeningDurationSeconds = value.HardeningDurationSeconds,
            RoundDurationSeconds = value.RoundDurationSeconds,
            AttackRewardMode = (AwdAttackRewardMode)value.AttackRewardMode,
            AttackPoints = value.AttackPoints,
            VictimDefensePoolPoints = value.VictimDefensePoolPoints,
            CheckerIntervalSeconds = value.CheckerIntervalSeconds,
            ServiceHealthyPoints = value.ServiceHealthyPoints,
            ServiceUnhealthyPenalty = value.ServiceUnhealthyPenalty
        }, value.FlagTemplate);

    private static CompetitionModeConfiguration Competition(AwdpConfiguration value) =>
        Common(new AwdpCompetitionModeConfiguration
        {
            RoundDurationSeconds = value.RoundDurationSeconds,
            BreakScoreCurve = Curve(value.Break),
            FixScoreCurve = Curve(value.Fix),
            FlagWrongPenalty = value.FlagWrongPenalty,
            ExploitSucceededPenalty = value.ExploitSucceededPenalty,
            ServiceAbnormalPenalty = value.ServiceAbnormalPenalty,
            RequireBreakBeforeFix = value.RequireBreakBeforeFix,
            MaxBreakSubmissions = value.MaxBreakSubmissions,
            MaxFixSubmissions = value.MaxFixSubmissions,
            EvaluationDispatchMode = (CompetitionEvaluationDispatchMode)value.EvaluationDispatchMode
        }, value.FlagTemplate);

    private static CompetitionModeConfiguration Competition(KohConfiguration value) =>
        new KohCompetitionModeConfiguration
        {
            PollIntervalSeconds = value.PollIntervalSeconds,
            ControlPointsPerInterval = value.ControlPointsPerInterval
        };

    private static CompetitionChallengeRules Rules(CtfChallengeConfiguration value) =>
        CommonRules(new CtfCompetitionChallengeRules
        {
            HasScoreCurve = value.ScoreCurve is not null,
            ScoreCurve = Curve(value.ScoreCurve),
            MaxFlagAttempts = value.MaxFlagAttempts,
            MaxPatchAttempts = value.MaxPatchAttempts,
            WrongSubmissionPenalty = value.WrongSubmissionPenalty,
            BloodRewards = (value.BloodRewards ?? []).Select((reward, position) =>
                new CompetitionChallengeBloodReward
                {
                    Position = position,
                    Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                    Value = reward.Value
                }).ToList()
        }, value.FlagTemplate);

    private static CompetitionChallengeRules Rules(AwdChallengeConfiguration value) =>
        CommonRules(new AwdCompetitionChallengeRules
        {
            AttackRewardMode = value.AttackRewardMode is null
                ? null : (AwdAttackRewardMode)value.AttackRewardMode.Value,
            AttackPoints = value.AttackPoints,
            VictimDefensePoolPoints = value.VictimDefensePoolPoints,
            CheckerIntervalSeconds = value.CheckerIntervalSeconds,
            ServiceHealthyPoints = value.ServiceHealthyPoints,
            ServiceUnhealthyPenalty = value.ServiceUnhealthyPenalty
        }, value.FlagTemplate);

    private static CompetitionChallengeRules Rules(AwdpChallengeConfiguration value) =>
        CommonRules(new AwdpCompetitionChallengeRules
        {
            HasBreakScoreCurve = value.Break is not null,
            BreakScoreCurve = Curve(value.Break),
            HasFixScoreCurve = value.Fix is not null,
            FixScoreCurve = Curve(value.Fix),
            RequireBreakBeforeFix = value.RequireBreakBeforeFix,
            MaxBreakSubmissions = value.MaxBreakSubmissions,
            MaxFixSubmissions = value.MaxFixSubmissions,
            FlagWrongPenalty = value.FlagWrongPenalty,
            ExploitSucceededPenalty = value.ExploitSucceededPenalty,
            ServiceAbnormalPenalty = value.ServiceAbnormalPenalty,
            EvaluationDispatchMode = value.EvaluationDispatchMode is null
                ? null
                : (CompetitionEvaluationDispatchMode)value.EvaluationDispatchMode.Value
        }, value.FlagTemplate);

    private static CompetitionChallengeRules Rules(KohChallengeConfiguration value) =>
        new KohCompetitionChallengeRules
        {
            PollIntervalSeconds = value.PollIntervalSeconds,
            ControlPointsPerInterval = value.ControlPointsPerInterval
        };

    private static ChallengeDefinition Definition(CtfChallengeConfiguration value) =>
        CommonDefinition(new CtfChallengeDefinition
        {
            InteractionKind = value.InteractionKind
        }, value.Runtime, value.PatchEntrypoint, value.PatchCommand,
            value.PatchTimeoutSeconds, value.Checker, value.ReadyTimeoutSeconds,
            value.MaximumPatchUploadBytes, value.FlagTemplate,
            value.CheckerFixInput);

    private static ChallengeDefinition Definition(AwdChallengeConfiguration value)
    {
        var result = CommonDefinition(new AwdChallengeDefinition
        {
            FlagInjectionCommand = value.FlagInjection?.Command,
            FlagInjectionTimeoutSeconds = value.FlagInjection?.TimeoutSeconds,
            FlagInjectionServiceName = value.FlagInjection?.ServiceName
        }, value.Runtime, null, null, null, value.Checker?.Job, null, null, value.FlagTemplate,
            false);
        if (result.Checker is not null)
            result.Checker.TargetServiceName = value.Checker?.TargetServiceName;
        return result;
    }

    private static ChallengeDefinition Definition(AwdpChallengeConfiguration value) =>
        CommonDefinition(new AwdpChallengeDefinition(), value.Runtime,
            value.PatchEntrypoint, value.PatchCommand, value.PatchTimeoutSeconds,
            value.Checker, value.ReadyTimeoutSeconds, value.MaximumPatchUploadBytes, value.FlagTemplate,
            value.CheckerFixInput);

    private static ChallengeDefinition Definition(KohChallengeConfiguration value) =>
        CommonDefinition(new KohChallengeDefinition(), value.Runtime,
            null, null, null, null, null, null, null, false);

    private static T Common<T>(T value, NoCTF.GameModes.Flags.PerTeamFlagTemplate? flag)
        where T : CompetitionModeConfiguration
    {
        value.FlagTemplate = Flag(flag);
        return value;
    }

    private static T CommonRules<T>(T value, NoCTF.GameModes.Flags.PerTeamFlagTemplate? flag)
        where T : CompetitionChallengeRules
    {
        value.HasFlagTemplate = flag is not null;
        value.FlagTemplate = Flag(flag);
        return value;
    }

    private static T CommonDefinition<T>(
        T value,
        ChallengeRuntimeTemplate? runtime,
        string? patchEntrypoint,
        IReadOnlyList<string>? patchCommand,
        int? patchTimeoutSeconds,
        NoCTF.Application.Runtime.Configuration.RunnerJobConfiguration? checker,
        int? readyTimeoutSeconds,
        long? maximumPatchUploadBytes,
        NoCTF.GameModes.Flags.PerTeamFlagTemplate? flagTemplate,
        bool checkerFixInput) where T : ChallengeDefinition
    {
        value.Runtime = Runtime(runtime);
        value.PatchEntrypoint = patchEntrypoint;
        value.PatchTimeoutSeconds = patchTimeoutSeconds;
        value.ReadyTimeoutSeconds = readyTimeoutSeconds;
        value.MaximumPatchUploadBytes = maximumPatchUploadBytes;
        value.HasFlagTemplate = flagTemplate is not null;
        value.FlagTemplate = Flag(flagTemplate);
        value.CheckerFixInput = checkerFixInput;
        value.Checker = checker is null ? null : new ChallengeCheckerDefinition
        {
            Image = checker.Image,
            TimeoutSeconds = checker.TimeoutSeconds
        };
        value.StringItems = (patchCommand ?? []).Select((item, position) =>
                new ChallengeDefinitionStringItem
                {
                    Kind = ChallengeDefinitionStringKind.PatchCommand,
                    Position = position,
                    Value = item
                })
            .Concat((checker?.Command ?? []).Select((item, position) =>
                new ChallengeDefinitionStringItem
                {
                    Kind = ChallengeDefinitionStringKind.CheckerCommand,
                    Position = position,
                    Value = item
                }))
            .Concat((checker?.Environment ?? new Dictionary<string, string>())
                .Select((item, position) => new ChallengeDefinitionStringItem
                {
                    Kind = ChallengeDefinitionStringKind.CheckerEnvironment,
                    Position = position,
                    Key = item.Key,
                    Value = item.Value
                })).ToList();
        return value;
    }

    private static ChallengeRuntimeTemplateEntity? Runtime(ChallengeRuntimeTemplate? value)
    {
        if (value is null) return null;
        ChallengeRuntimeTemplateEntity runtime = value.Definition switch
        {
            ContainerRuntimeDefinition container => new ContainerChallengeRuntimeTemplate
            {
                Services = container.Services.Select((service, position) => new ChallengeRuntimeService
                {
                    Name = service.Name, Position = position, Image = service.Image, CpuCores = service.CpuCores, MemoryMiB = service.MemoryMiB,
                    FlagEnvironmentVariableName = service.FlagEnvironmentVariableName,
                    Commands = (service.Command ?? []).Select((item, index) => new ChallengeRuntimeServiceCommand { Position = index, Value = item })
                        .Concat((service.Arguments ?? []).Select((item, index) => new ChallengeRuntimeServiceCommand { Position = index, Value = item, IsArgument = true })).ToList(),
                    Environment = (service.Environment ?? new Dictionary<string, string>()).Select(item => new ChallengeRuntimeServiceEnvironment { Name = item.Key, Value = item.Value }).ToList(),
                    InternalPorts = (service.InternalPorts ?? []).Select(port => new ChallengeRuntimeServicePort { Port = port }).ToList()
                }).ToList(),
                EgressPolicy = (PersistedRuntimeEgressPolicy)container.EgressPolicy
            },
            OvaRuntimeDefinition ova => new OvaChallengeRuntimeTemplate
            {
                OvaSourceUrl = ova.OvaSourceUrl,
                Sha256 = ova.Sha256
            },
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
        runtime.Allocation = (PersistedRuntimeAllocation)value.Allocation;
        if (runtime is OvaChallengeRuntimeTemplate ovaResources && value.Limits is { } limits)
        {
            ovaResources.HasExplicitLimits = true;
            ovaResources.Limits = new() { MemoryBytes = limits.MemoryBytes, CpuMillicores = limits.CpuMillicores, PidsLimit = limits.PidsLimit };
        }
        runtime.TtlSeconds = value.TtlSeconds;
        runtime.OperationTimeoutSeconds = value.OperationTimeoutSeconds;
        runtime.FlagSource = (PersistedRuntimeFlagSource)value.FlagSource;
        runtime.UrlBindings = (value.UrlBindings ?? []).Select((binding, position) =>
            new ChallengeRuntimeUrlBinding
            {
                Position = position,
                UrlTemplate = binding.UrlTemplate,
                Exposure = (PersistedRuntimeExposure)binding.Exposure,
                ContainerPort = binding.ContainerPort,
                ServiceName = binding.ServiceName ?? (value.Definition is ContainerRuntimeDefinition ? "main" : null),
                VmId = binding.VmId,
                GuestPort = binding.GuestPort
            }).ToList();
        if (value.ControlCheckUrlBinding is not null)
        {
            var binding = value.ControlCheckUrlBinding;
            runtime.UrlBindings.Add(new ChallengeRuntimeUrlBinding
            {
                Position = runtime.UrlBindings.Count,
                IsControlCheck = true,
                UrlTemplate = binding.UrlTemplate,
                Exposure = (PersistedRuntimeExposure)binding.Exposure,
                ContainerPort = binding.ContainerPort,
                ServiceName = binding.ServiceName ?? (value.Definition is ContainerRuntimeDefinition ? "main" : null),
                VmId = binding.VmId,
                GuestPort = binding.GuestPort
            });
        }
        return runtime;
    }

    private static ScoreCurveValue Curve(ScoreCurveConfiguration? value) => value is null
        ? new()
        : new()
        {
            InitialPoints = value.InitialPoints,
            MinimumPoints = value.MinimumPoints,
            DecayTeamCount = value.DecayTeamCount,
            DecayMode = (PersistedScoreDecayMode)value.DecayMode,
            CustomExpression = value.CustomExpression
        };

    private static FlagTemplateValue Flag(NoCTF.GameModes.Flags.PerTeamFlagTemplate? value) =>
        value is null
            ? new()
            : new()
            {
                Header = value.Header,
                BodyTemplate = value.BodyTemplate,
                LeetLiteralText = value.LeetLiteralText
            };

    private static T Parse<T>(string json, int expectedSchemaVersion)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Test configuration must be a JSON object.");
        _ = expectedSchemaVersion;
        root.Remove("schemaVersion");
        return root.Deserialize<T>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidOperationException("Test configuration cannot be null.");
    }
}

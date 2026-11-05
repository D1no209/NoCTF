using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;

namespace NoCTF.CurrentImport;

internal static class LegacyConverters
{
    [JsonConverter(typeof(JsonStringEnumConverter<ArchivedRuntimePurpose>))]
    private enum ArchivedRuntimePurpose : short
    {
        Player = 0, AwdpTarget = 1, Practice = 2, AwdpAttack = 3, TemplateTest = 4, PatchVerificationTarget = 5
    }

    public static RuntimePurpose RuntimeUse(JsonElement value) => value.Deserialize<ArchivedRuntimePurpose>() switch
    {
        ArchivedRuntimePurpose.Player => RuntimePurpose.Player,
        ArchivedRuntimePurpose.AwdpTarget => RuntimePurpose.FixTarget,
        ArchivedRuntimePurpose.Practice => RuntimePurpose.Practice,
        ArchivedRuntimePurpose.AwdpAttack => RuntimePurpose.AttackTarget,
        ArchivedRuntimePurpose.TemplateTest => RuntimePurpose.TemplateTest,
        ArchivedRuntimePurpose.PatchVerificationTarget => throw new InvalidOperationException(
            "CTF PatchVerification Runtime resources cannot be imported into the capability model."),
        _ => throw new InvalidOperationException("The archive contains an unsupported Runtime purpose.")
    };

    [JsonConverter(typeof(JsonStringEnumConverter<ArchivedGameplayKind>))]
    private enum ArchivedGameplayKind : short
    {
        FlagAttempt = 0, BreakAttempt = 1, FixAttempt = 2, HintUnlock = 3, ManualAdjustment = 4,
        AwdServiceTransition = 5, KohControlObservation = 6, AttachmentDownload = 7, WriteUpUnlock = 8,
        TheoryPaperSubmission = 9
    }

    public static GameplayFactKind GameplayKind(JsonElement value) => value.Deserialize<ArchivedGameplayKind>() switch
    {
        ArchivedGameplayKind.FlagAttempt or ArchivedGameplayKind.BreakAttempt => GameplayFactKind.BreakAttempt,
        ArchivedGameplayKind.FixAttempt => GameplayFactKind.FixAttempt,
        ArchivedGameplayKind.HintUnlock => GameplayFactKind.HintUnlock,
        ArchivedGameplayKind.ManualAdjustment => GameplayFactKind.ManualAdjustment,
        ArchivedGameplayKind.AwdServiceTransition => GameplayFactKind.CheckObservation,
        ArchivedGameplayKind.KohControlObservation => GameplayFactKind.ControlObservation,
        ArchivedGameplayKind.AttachmentDownload => GameplayFactKind.AttachmentDownload,
        ArchivedGameplayKind.WriteUpUnlock => GameplayFactKind.WriteUpUnlock,
        ArchivedGameplayKind.TheoryPaperSubmission => throw new InvalidOperationException(
            "Theory paper scoring facts require explicit questionnaire assignments and cannot be imported as competition facts."),
        _ => throw new InvalidOperationException("The archive contains an unsupported gameplay fact kind.")
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CompetitionModeConfiguration Competition(
        Guid competitionId,
        GameMode mode,
        string json) => mode switch
    {
        GameMode.Ctf => CtfCompetition(competitionId, json),
        GameMode.Awd => AwdCompetition(competitionId, json),
        GameMode.Awdp => AwdpCompetition(competitionId, json),
        GameMode.Koh => KohCompetition(competitionId, json),
        _ => throw new InvalidOperationException($"Unsupported competition mode {mode}.")
    };

    private static CtfCompetitionModeConfiguration CtfCompetition(
        Guid competitionId,
        string json)
    {
        var value = Parse<CtfConfiguration>(json, 2);
        return new()
        {
            CompetitionId = competitionId,
            DefaultScoreCurve = Curve(value.DefaultScoreCurve),
            ScoreSettlementMode = value.ScoreSettlementMode,
            BloodRewards = value.BloodRewards.Select((reward, position) =>
                new CompetitionBloodReward
                {
                    CompetitionId = competitionId,
                    Position = position,
                    Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                    Value = reward.Value
                }).ToList(),
            WrongSubmissionPenalty = value.WrongSubmissionPenalty,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static AwdCompetitionModeConfiguration AwdCompetition(
        Guid competitionId,
        string json)
    {
        var value = Parse<AwdConfiguration>(json, 2);
        return new()
        {
            CompetitionId = competitionId,
            HardeningDurationSeconds = value.HardeningDurationSeconds,
            RoundDurationSeconds = value.RoundDurationSeconds,
            AttackRewardMode = (AwdAttackRewardMode)value.AttackRewardMode,
            AttackPoints = value.AttackPoints,
            VictimDefensePoolPoints = value.VictimDefensePoolPoints,
            CheckerIntervalSeconds = value.CheckerIntervalSeconds,
            ServiceHealthyPoints = value.ServiceHealthyPoints,
            ServiceUnhealthyPenalty = value.ServiceUnhealthyPenalty,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static AwdpCompetitionModeConfiguration AwdpCompetition(
        Guid competitionId,
        string json)
    {
        var value = Parse<AwdpConfiguration>(json, 4);
        return new()
        {
            CompetitionId = competitionId,
            RoundDurationSeconds = value.RoundDurationSeconds,
            BreakScoreCurve = Curve(value.Break),
            FixScoreCurve = Curve(value.Fix),
            FlagWrongPenalty = value.FlagWrongPenalty,
            ExploitSucceededPenalty = value.ExploitSucceededPenalty,
            ServiceAbnormalPenalty = value.ServiceAbnormalPenalty,
            RequireBreakBeforeFix = value.RequireBreakBeforeFix,
            MaxBreakSubmissions = value.MaxBreakSubmissions,
            MaxFixSubmissions = value.MaxFixSubmissions,
            EvaluationDispatchMode = (CompetitionEvaluationDispatchMode)value.EvaluationDispatchMode,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static KohCompetitionModeConfiguration KohCompetition(
        Guid competitionId,
        string json)
    {
        var value = Parse<KohConfiguration>(json, 1);
        return new()
        {
            CompetitionId = competitionId,
            PollIntervalSeconds = value.PollIntervalSeconds,
            ControlPointsPerInterval = value.ControlPointsPerInterval
        };
    }

    public static CompetitionChallengeRules Rules(
        Guid competitionChallengeId,
        GameMode mode,
        string json) => mode switch
    {
        GameMode.Ctf => CtfRules(competitionChallengeId, json),
        GameMode.Awd => AwdRules(competitionChallengeId, json),
        GameMode.Awdp => AwdpRules(competitionChallengeId, json),
        GameMode.Koh => KohRules(competitionChallengeId, json),
        _ => throw new InvalidOperationException($"Unsupported rules mode {mode}.")
    };

    private static CtfCompetitionChallengeRules CtfRules(
        Guid competitionChallengeId,
        string json)
    {
        var value = Parse<CtfChallengeConfiguration>(json, 2);
        return new()
        {
            CompetitionChallengeId = competitionChallengeId,
            HasScoreCurve = value.ScoreCurve is not null,
            ScoreSettlementMode = value.ScoreSettlementMode,
            ScoreCurve = Curve(value.ScoreCurve),
            Runtime = RuntimeRules(value.Runtime, new CtfCompetitionChallengeRules().Runtime),
            MaxFlagAttempts = value.MaxFlagAttempts,
            WrongSubmissionPenalty = value.WrongSubmissionPenalty,
            BloodRewards = (value.BloodRewards ?? []).Select((reward, position) =>
                new CompetitionChallengeBloodReward
                {
                    CompetitionChallengeId = competitionChallengeId,
                    Position = position,
                    Policy = (CompetitionBloodRewardPolicy)reward.Policy,
                    Value = reward.Value
                }).ToList(),
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static AwdCompetitionChallengeRules AwdRules(
        Guid competitionChallengeId,
        string json)
    {
        var value = Parse<AwdChallengeConfiguration>(json, 2);
        return new()
        {
            CompetitionChallengeId = competitionChallengeId,
            AttackRewardMode = value.AttackRewardMode is null
                ? null : (AwdAttackRewardMode)value.AttackRewardMode.Value,
            Runtime = RuntimeRules(value.Runtime, new AwdCompetitionChallengeRules().Runtime),
            AttackPoints = value.AttackPoints,
            VictimDefensePoolPoints = value.VictimDefensePoolPoints,
            CheckerIntervalSeconds = value.CheckerIntervalSeconds,
            ServiceHealthyPoints = value.ServiceHealthyPoints,
            ServiceUnhealthyPenalty = value.ServiceUnhealthyPenalty,
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static AwdpCompetitionChallengeRules AwdpRules(
        Guid competitionChallengeId,
        string json)
    {
        var value = Parse<AwdpChallengeConfiguration>(json, 4);
        return new()
        {
            CompetitionChallengeId = competitionChallengeId,
            HasBreakScoreCurve = value.Break is not null,
            Runtime = RuntimeRules(value.Runtime, new AwdpCompetitionChallengeRules().Runtime),
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
                ? null : (CompetitionEvaluationDispatchMode)value.EvaluationDispatchMode.Value,
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static KohCompetitionChallengeRules KohRules(
        Guid competitionChallengeId,
        string json)
    {
        var value = Parse<KohChallengeConfiguration>(json, 1);
        return new()
        {
            CompetitionChallengeId = competitionChallengeId,
            PollIntervalSeconds = value.PollIntervalSeconds,
            Runtime = RuntimeRules(value.Runtime, new KohCompetitionChallengeRules().Runtime),
            ControlPointsPerInterval = value.ControlPointsPerInterval
        };
    }

    public static ChallengeDefinition Definition(
        Guid challengeId,
        GameMode mode,
        string json) => mode switch
    {
        GameMode.Ctf => CtfDefinition(challengeId, json),
        GameMode.Awd => AwdDefinition(challengeId, json),
        GameMode.Awdp => AwdpDefinition(challengeId, json),
        GameMode.Koh => KohDefinition(challengeId, json),
        _ => throw new InvalidOperationException($"Unsupported definition mode {mode}.")
    };

    private static ChallengeDefinition CtfDefinition(Guid challengeId, string json)
    {
        var value = Parse<CtfChallengeConfiguration>(json, 3);
        return new ChallengeDefinition
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            Capabilities = [Break(challengeId, value.FlagTemplate, value.Runtime)]
        };
    }

    private static ChallengeDefinition AwdDefinition(Guid challengeId, string json)
    {
        var value = Parse<AwdChallengeConfiguration>(json, 2);
        var attack = Break(challengeId, value.FlagTemplate, value.Runtime);
        attack.InjectionCommand = value.FlagInjection?.Command;
        attack.InjectionTimeoutSeconds = value.FlagInjection?.TimeoutSeconds ?? 30;
        attack.InjectionServiceName = value.FlagInjection?.ServiceName;
        attack.FlagStores = (value.FlagStores ?? []).Select((store, position) => new FlagStoreDefinition
        {
            ChallengeId = challengeId, Id = store.Id, Position = position, Name = store.Name, TargetServiceName = store.TargetServiceName,
            DeliveryKind = (FlagDeliveryKind)store.DeliveryMode, InjectionCommand = store.InjectionCommand,
            InjectionTimeoutSeconds = store.InjectionTimeoutSeconds, PreservesHistoricalFlags = store.PreservesHistoricalFlags
        }).ToList();
        var capabilities = new List<ChallengeCapabilityDefinition> { attack };
        if (value.Checker is { } checker) capabilities.Add(Checker(challengeId, checker.Job,
            TargetService(value.Runtime, checker.TargetServiceName), [CheckItemKind.Connection, CheckItemKind.Functionality]));
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            Capabilities = capabilities
        };
    }

    private static ChallengeDefinition AwdpDefinition(Guid challengeId, string json)
    {
        var value = Parse<AwdpChallengeConfiguration>(json, 4);
        var capabilities = new List<ChallengeCapabilityDefinition> { Break(challengeId, value.FlagTemplate, value.Runtime) };
        if (value.Runtime is not null)
        {
            var service = TargetService(value.Runtime);
            capabilities.Add(new FixCapabilityDefinition
            {
                ChallengeId = challengeId, TargetServiceName = service, Entrypoint = value.PatchEntrypoint ?? "fix.sh",
                TimeoutSeconds = value.PatchTimeoutSeconds ?? 60, MaximumUploadBytes = value.MaximumPatchUploadBytes ?? 256L * 1024 * 1024,
                Command = (value.PatchCommand ?? []).Select((argument, position) => new FixCommandArgument
                    { ChallengeId = challengeId, Position = position, Value = argument }).ToList()
            });
            if (value.Checker is { } job)
            {
                var check = Checker(challengeId, job, service, [CheckItemKind.Functionality, CheckItemKind.Vulnerability]);
                check.ReadyTimeoutSeconds = value.ReadyTimeoutSeconds ?? 30; check.AcceptsFixInput = value.CheckerFixInput;
                capabilities.Add(check);
            }
        }
        else if (value.PatchEntrypoint is not null || value.PatchCommand is { Count: > 0 } || value.Checker is not null)
            throw new InvalidOperationException("AWDP repair configuration requires a Runtime during import.");
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            Capabilities = capabilities
        };
    }

    private static ChallengeDefinition KohDefinition(Guid challengeId, string json)
    {
        var value = Parse<KohChallengeConfiguration>(json, 1);
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            Capabilities = [new ControlCapabilityDefinition
            {
                ChallengeId = challengeId, UrlTemplate = value.Runtime?.ControlCheckUrlBinding?.UrlTemplate ?? string.Empty,
                ServiceName = value.Runtime?.ControlCheckUrlBinding?.ServiceName, ContainerPort = value.Runtime?.ControlCheckUrlBinding?.ContainerPort,
                VmId = value.Runtime?.ControlCheckUrlBinding?.VmId, GuestPort = value.Runtime?.ControlCheckUrlBinding?.GuestPort
            }]
        };
    }

    private static CheckCapabilityDefinition Checker(
        Guid challengeId,
        RunnerJobConfiguration job,
        string targetServiceName,
        IReadOnlyList<CheckItemKind> required) => new()
    {
        ChallengeId = challengeId,
        Image = job.Image,
        TimeoutSeconds = job.TimeoutSeconds,
        TargetServiceName = targetServiceName,
        Command = (job.Command ?? []).Select((value, position) => new CheckCommandArgument { ChallengeId = challengeId, Position = position, Value = value }).ToList(),
        Environment = (job.Environment ?? new Dictionary<string, string>()).Select(item => new CheckEnvironmentVariable { ChallengeId = challengeId, Name = item.Key, Value = item.Value }).ToList(),
        Requirements = required.Select(kind => new CheckRequirement { ChallengeId = challengeId, Kind = kind }).ToList()
    };

    private static BreakCapabilityDefinition Break(Guid challengeId, NoCTF.GameModes.Flags.PerTeamFlagTemplate? template, ChallengeRuntimeTemplate? runtime) => new()
    {
        ChallengeId = challengeId, HasFlagTemplate = template is not null, FlagTemplate = Flag(template),
        EnvironmentTargets = runtime?.Definition is ContainerRuntimeDefinition container
            ? container.Services.Where(service => service.FlagEnvironmentVariableName is not null).Select((service, position) => new FlagEnvironmentTarget
                { ChallengeId = challengeId, Position = position, ServiceName = service.Name, VariableName = service.FlagEnvironmentVariableName! }).ToList() : []
    };

    private static string TargetService(ChallengeRuntimeTemplate? runtime, string? explicitName = null)
    {
        if (runtime?.Definition is not ContainerRuntimeDefinition container)
            throw new InvalidOperationException("Capability import requires a Container Runtime target.");
        if (explicitName is { Length: > 0 } && container.Services.Any(service => service.Name == explicitName)) return explicitName;
        return container.Services.Count == 1 ? container.Services[0].Name
            : throw new InvalidOperationException("A multi-service capability requires an explicit target service during import.");
    }

    public static CompetitionTrackConfiguration Tracks(GameMode mode, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Current competitions require an explicit track configuration.");
        var configuration = Parse<CompetitionTrackConfiguration>(json, 1);
        _ = CompetitionTrackConfiguration.FromPersisted(mode, configuration.Tracks);
        return configuration;
    }

    public static SsoConfiguration Sso(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("SSO configuration must be an object.");
        EnsureVersion(value, 1);
        var result = new SsoConfiguration
        {
            Enabled = value.GetProperty("enabled").GetBoolean(),
            PublicBaseUrl = value.GetProperty("publicBaseUrl").GetString() ?? string.Empty
        };
        foreach (var provider in value.GetProperty("providers").EnumerateArray())
        {
            var protocol = provider.GetProperty("protocol").GetInt16();
            SsoProviderConfiguration converted = protocol switch
            {
                0 => Oidc(provider),
                1 => Cas(provider),
                _ => throw new InvalidOperationException($"Unknown SSO protocol {protocol}.")
            };
            converted.Id = provider.GetProperty("id").GetGuid();
            converted.PlatformSettingsId = 1;
            converted.Name = provider.GetProperty("name").GetString() ?? string.Empty;
            converted.IconUrl = OptionalString(provider, "iconUrl");
            converted.Enabled = provider.GetProperty("enabled").GetBoolean();
            converted.AllowLogin = provider.GetProperty("allowLogin").GetBoolean();
            converted.AllowBinding = provider.GetProperty("allowBinding").GetBoolean();
            converted.TimeoutSeconds = provider.GetProperty("timeoutSeconds").GetInt32();
            converted.AllowedHosts = provider.GetProperty("allowedHosts")
                .EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
            result.Providers.Add(converted);
        }
        return result;
    }

    private static OidcSsoProviderConfiguration Oidc(JsonElement provider)
    {
        var oidc = provider.GetProperty("oidc");
        return new()
        {
            Issuer = oidc.GetProperty("issuer").GetString() ?? string.Empty,
            DiscoveryUrl = oidc.GetProperty("discoveryUrl").GetString() ?? string.Empty,
            ClientId = oidc.GetProperty("clientId").GetString() ?? string.Empty,
            ClientSecretCiphertext = OptionalBytes(oidc, "clientSecretCiphertext"),
            Scopes = oidc.GetProperty("scopes").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty).ToArray(),
            ReadUserInfo = oidc.GetProperty("readUserInfo").GetBoolean(),
            DisplayNameClaim = oidc.GetProperty("displayNameClaim").GetString() ?? "name"
        };
    }

    private static CasSsoProviderConfiguration Cas(JsonElement provider)
    {
        var cas = provider.GetProperty("cas");
        return new()
        {
            IdentityNamespace = cas.GetProperty("identityNamespace").GetString() ?? string.Empty,
            LoginUrl = cas.GetProperty("loginUrl").GetString() ?? string.Empty,
            ServiceValidateUrl = cas.GetProperty("serviceValidateUrl").GetString() ?? string.Empty,
            DisplayNameAttribute = cas.GetProperty("displayNameAttribute").GetString() ?? "displayName"
        };
    }

    private static ChallengeRuntimeTemplateEntity? Runtime(
        Guid challengeId,
        ChallengeRuntimeTemplate? value)
    {
        if (value is null) return null;
        ChallengeRuntimeTemplateEntity runtime = value.Definition switch
        {
            ContainerRuntimeDefinition container => new ContainerChallengeRuntimeTemplate
            {
                Services = container.Services.Select((service, position) => new ChallengeRuntimeService
                {
                    ChallengeId = challengeId,
                    Name = service.Name,
                    Position = position,
                    Image = service.Image,
                    CpuCores = service.CpuCores,
                    MemoryMiB = service.MemoryMiB,
                    Commands = (service.Command ?? []).Select((item, index) =>
                        new ChallengeRuntimeServiceCommand
                        { ChallengeId = challengeId, ServiceName = service.Name, Position = index, Value = item })
                        .Concat((service.Arguments ?? []).Select((item, index) =>
                            new ChallengeRuntimeServiceCommand
                            { ChallengeId = challengeId, ServiceName = service.Name, IsArgument = true, Position = index, Value = item }))
                        .ToList(),
                    Environment = (service.Environment ?? new Dictionary<string, string>())
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => new ChallengeRuntimeServiceEnvironment
                        { ChallengeId = challengeId, ServiceName = service.Name, Name = item.Key, Value = item.Value }).ToList(),
                    InternalPorts = (service.InternalPorts ?? []).Select(port =>
                        new ChallengeRuntimeServicePort
                        { ChallengeId = challengeId, ServiceName = service.Name, Port = port }).ToList()
                }).ToList(),
                EgressPolicy = (PersistedRuntimeEgressPolicy)container.EgressPolicy
            },
            OvaRuntimeDefinition ova => new OvaChallengeRuntimeTemplate
            {
                OvaSourceUrl = ova.OvaSourceUrl,
                Sha256 = ova.Sha256
            },
            _ => throw new InvalidOperationException(
                $"Unsupported runtime definition {value.Definition.GetType().Name}.")
        };
        runtime.ChallengeId = challengeId;
        if (runtime is OvaChallengeRuntimeTemplate resources)
        {
            resources.HasExplicitLimits = value.Limits is not null;
            resources.Limits = value.Limits is null ? new RuntimeResourceLimitsValue() : Limits(value.Limits);
        }
        runtime.OperationTimeoutSeconds = value.OperationTimeoutSeconds;
        runtime.UrlBindings = (value.UrlBindings ?? []).Select((binding, position) =>
            new ChallengeRuntimeUrlBinding
            {
                Position = position,
                UrlTemplate = binding.UrlTemplate,
                Exposure = (PersistedRuntimeExposure)binding.Exposure,
                ContainerPort = binding.ContainerPort,
                ServiceName = binding.ServiceName,
                VmId = binding.VmId,
                GuestPort = binding.GuestPort
            }).ToList();
        ChallengeDefinitionGraph.AssignChallengeId(new ChallengeDefinition
        {
            ChallengeId = challengeId,
            Runtime = runtime
        }, challengeId);
        return runtime;
    }

    private static RuntimeResourceLimitsValue Limits(RuntimeResourceLimits value) => new()
    {
        MemoryBytes = value.MemoryBytes,
        CpuMillicores = value.CpuMillicores,
        PidsLimit = value.PidsLimit
    };

    private static CompetitionChallengeRuntimeRules RuntimeRules(ChallengeRuntimeTemplate? value, CompetitionChallengeRuntimeRules defaults) =>
        value is null ? defaults : new() { Allocation = (PersistedRuntimeAllocation)value.Allocation,
            FlagSource = (PersistedRuntimeFlagSource)value.FlagSource, TtlSeconds = value.TtlSeconds };

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

    private static T Parse<T>(string json, int expectedVersion)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException($"{typeof(T).Name} must be a JSON object.");
        if (!root.TryGetPropertyValue("schemaVersion", out var versionNode)
            || versionNode?.GetValue<int>() != expectedVersion)
            throw new InvalidOperationException(
                $"{typeof(T).Name} requires schemaVersion {expectedVersion}.");
        root.Remove("schemaVersion");
        if (typeof(T) == typeof(CtfChallengeConfiguration))
        {
            if (root.Remove("interactionKind", out var interaction) && interaction is not null
                && interaction.ToJsonString() is not ("0" or "\"FlagSubmission\""))
                throw new InvalidOperationException("CTF PatchVerification is no longer supported and cannot be imported as Break.");
            foreach (var field in new[] { "maxPatchAttempts", "patchEntrypoint", "patchCommand", "patchTimeoutSeconds", "checker", "readyTimeoutSeconds", "maximumPatchUploadBytes", "checkerFixInput" })
            {
                if (!root.Remove(field, out var content) || content is null || content is JsonArray { Count: 0 }
                    || field == "checkerFixInput" && content.ToJsonString() == "false") continue;
                throw new InvalidOperationException($"CTF repair field '{field}' cannot be imported into Break-only CTF rules.");
            }
        }
        return root.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"{typeof(T).Name} cannot be null.");
    }

    private static void EnsureVersion(JsonElement value, int expectedVersion)
    {
        if (!value.TryGetProperty("schemaVersion", out var version)
            || version.GetInt32() != expectedVersion)
            throw new InvalidOperationException($"Expected schemaVersion {expectedVersion}.");
    }

    private static string? OptionalString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static byte[]? OptionalBytes(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetBytesFromBase64()
            : null;
}

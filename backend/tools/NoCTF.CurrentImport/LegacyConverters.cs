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

namespace NoCTF.CurrentImport;

internal static class LegacyConverters
{
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
            ScoreCurve = Curve(value.ScoreCurve),
            MaxFlagAttempts = value.MaxFlagAttempts,
            MaxPatchAttempts = value.MaxPatchAttempts,
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

    private static CtfChallengeDefinition CtfDefinition(Guid challengeId, string json)
    {
        var value = Parse<CtfChallengeConfiguration>(json, 3);
        var result = new CtfChallengeDefinition
        {
            ChallengeId = challengeId,
            InteractionKind = value.InteractionKind,
            Runtime = Runtime(challengeId, value.Runtime),
            PatchEntrypoint = value.PatchEntrypoint,
            PatchTimeoutSeconds = value.PatchTimeoutSeconds,
            ReadyTimeoutSeconds = value.ReadyTimeoutSeconds,
            MaximumPatchUploadBytes = value.MaximumPatchUploadBytes,
            CheckerFixInput = value.CheckerFixInput,
            CheckerAllowRoot = value.CheckerAllowRoot,
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate),
            Checker = value.Checker is null ? null : new ChallengeCheckerDefinition
            {
                ChallengeId = challengeId,
                Image = value.Checker.Image,
                TimeoutSeconds = value.Checker.TimeoutSeconds,
                TargetServiceName = null
            }
        };
        result.StringItems = (value.PatchCommand ?? []).Select((item, position) =>
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

    private static AwdChallengeDefinition AwdDefinition(Guid challengeId, string json)
    {
        var value = Parse<AwdChallengeConfiguration>(json, 2);
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            Checker = Checker(challengeId, value.Checker?.Job,
                value.Checker?.TargetServiceName),
            StringItems = CheckerItems(challengeId, value.Checker?.Job).ToList(),
            FlagInjectionCommand = value.FlagInjection?.Command,
            FlagInjectionTimeoutSeconds = value.FlagInjection?.TimeoutSeconds,
            FlagInjectionServiceName = value.FlagInjection?.ServiceName,
            CheckerAllowRoot = value.CheckerAllowRoot,
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate)
        };
    }

    private static AwdpChallengeDefinition AwdpDefinition(Guid challengeId, string json)
    {
        var value = Parse<AwdpChallengeConfiguration>(json, 4);
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime),
            PatchEntrypoint = value.PatchEntrypoint,
            PatchTimeoutSeconds = value.PatchTimeoutSeconds,
            ReadyTimeoutSeconds = value.ReadyTimeoutSeconds,
            MaximumPatchUploadBytes = value.MaximumPatchUploadBytes,
            CheckerFixInput = value.CheckerFixInput,
            CheckerAllowRoot = value.CheckerAllowRoot,
            HasFlagTemplate = value.FlagTemplate is not null,
            FlagTemplate = Flag(value.FlagTemplate),
            Checker = Checker(challengeId, value.Checker),
            StringItems = (value.PatchCommand ?? []).Select((item, position) =>
                    new ChallengeDefinitionStringItem
                    {
                        ChallengeId = challengeId,
                        Kind = ChallengeDefinitionStringKind.PatchCommand,
                        Position = position,
                        Value = item
                    })
                .Concat(CheckerItems(challengeId, value.Checker))
                .ToList()
        };
    }

    private static KohChallengeDefinition KohDefinition(Guid challengeId, string json)
    {
        var value = Parse<KohChallengeConfiguration>(json, 1);
        return new()
        {
            ChallengeId = challengeId,
            Runtime = Runtime(challengeId, value.Runtime)
        };
    }

    private static ChallengeCheckerDefinition? Checker(
        Guid challengeId,
        RunnerJobConfiguration? job,
        string? targetServiceName = null) => job is null ? null : new()
    {
        ChallengeId = challengeId,
        Image = job.Image,
        TimeoutSeconds = job.TimeoutSeconds,
        TargetServiceName = targetServiceName
    };

    private static IEnumerable<ChallengeDefinitionStringItem> CheckerItems(
        Guid challengeId,
        RunnerJobConfiguration? job) =>
        (job?.Command ?? []).Select((item, position) => new ChallengeDefinitionStringItem
        {
            ChallengeId = challengeId,
            Kind = ChallengeDefinitionStringKind.CheckerCommand,
            Position = position,
            Value = item
        }).Concat((job?.Environment ?? new Dictionary<string, string>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select((item, position) => new ChallengeDefinitionStringItem
            {
                ChallengeId = challengeId,
                Kind = ChallengeDefinitionStringKind.CheckerEnvironment,
                Position = position,
                Key = item.Key,
                Value = item.Value
            }));

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
                Image = container.Image,
                Security = new ContainerSecurityPolicyValue
                {
                    NoNewPrivileges = container.Security.NoNewPrivileges,
                    ReadonlyRootfs = container.Security.ReadonlyRootfs,
                    RunAsNonRoot = container.Security.RunAsNonRoot
                },
                FlagEnvironmentVariableName = container.FlagEnvironmentVariableName,
                Capabilities = container.Security.CapDrop
                    .Where(name => !string.Equals(name, "ALL", StringComparison.OrdinalIgnoreCase))
                    .Select(name =>
                        new ChallengeRuntimeCapability { Name = name })
                    .Concat(container.Security.CapAdd.Select(name =>
                        new ChallengeRuntimeCapability { Name = name, Add = true })).ToList(),
                PortMappings = (container.PortMappings ?? new Dictionary<int, int>()).Select(pair =>
                    new ChallengeRuntimePortMapping
                    {
                        ContainerPort = pair.Key,
                        HostPort = pair.Value
                    }).ToList(),
                InternalPorts = (container.InternalPorts ?? []).Select(port =>
                    new ChallengeRuntimeInternalPort { Port = port }).ToList(),
                CommandItems = (container.Command ?? []).Select((item, position) =>
                    new ChallengeRuntimeCommandItem { Position = position, Value = item }).ToList(),
                KeyValues = Values(container.Environment, ChallengeRuntimeKeyValueKind.Environment)
                    .Concat(Values(container.Labels, ChallengeRuntimeKeyValueKind.Label)).ToList(),
                EgressPolicy = (PersistedRuntimeEgressPolicy)container.EgressPolicy
            },
            ComposeRuntimeDefinition compose => new ComposeChallengeRuntimeTemplate
            {
                ComposeYaml = compose.ComposeYaml,
                ServiceResources = compose.ServiceResources.Select(pair =>
                    new ComposeServiceResource
                    {
                        ServiceName = pair.Key,
                        Limits = Limits(pair.Value)
                    }).ToList(),
                KeyValues = Values(compose.Environment, ChallengeRuntimeKeyValueKind.Environment)
                    .Concat(Values(compose.Labels, ChallengeRuntimeKeyValueKind.Label))
                    .Concat(Values(compose.FlagEnvironmentVariables,
                        ChallengeRuntimeKeyValueKind.FlagEnvironmentVariable)).ToList(),
                EgressPolicy = (PersistedRuntimeEgressPolicy)compose.EgressPolicy
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
        runtime.Allocation = (PersistedRuntimeAllocation)value.Allocation;
        runtime.HasExplicitLimits = value.Limits is not null;
        runtime.Limits = value.Limits is null ? new RuntimeResourceLimitsValue() : Limits(value.Limits);
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
                ServiceName = binding.ServiceName,
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
                ServiceName = binding.ServiceName,
                VmId = binding.VmId,
                GuestPort = binding.GuestPort
            });
        }
        ChallengeDefinitionGraph.AssignChallengeId(new CtfChallengeDefinition
        {
            ChallengeId = challengeId,
            Runtime = runtime
        }, challengeId);
        return runtime;
    }

    private static IEnumerable<ChallengeRuntimeKeyValue> Values(
        IReadOnlyDictionary<string, string>? values,
        ChallengeRuntimeKeyValueKind kind) =>
        (values ?? new Dictionary<string, string>())
        .OrderBy(item => item.Key, StringComparer.Ordinal)
        .Select(item => new ChallengeRuntimeKeyValue
        {
            Kind = kind,
            Key = item.Key,
            Value = item.Value
        });

    private static RuntimeResourceLimitsValue Limits(RuntimeResourceLimits value) => new()
    {
        MemoryBytes = value.MemoryBytes,
        NanoCpus = value.NanoCpus,
        PidsLimit = value.PidsLimit
    };

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

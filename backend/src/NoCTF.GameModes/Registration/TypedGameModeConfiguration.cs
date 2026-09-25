using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Registration;

/// <summary>Maps the provider-neutral relational domain model into mode policy values.</summary>
public static class TypedGameModeConfiguration
{
    public static CtfConfiguration Ctf(CtfCompetitionModeConfiguration value) => new(
        Curve(value.DefaultScoreCurve),
        value.BloodRewards.OrderBy(item => item.Position).Select(item =>
            new BloodReward((BloodRewardPolicy)item.Policy, item.Value)).ToArray(),
        value.WrongSubmissionPenalty,
        Flag(value.FlagTemplate));

    public static CtfChallengeConfiguration Ctf(CtfCompetitionChallengeRules value) => new(
        value.HasScoreCurve ? Curve(value.ScoreCurve) : null,
        value.BloodRewards.Count == 0 ? null : value.BloodRewards.OrderBy(item => item.Position)
            .Select(item => new BloodReward((BloodRewardPolicy)item.Policy, item.Value)).ToArray(),
        value.MaxFlagAttempts,
        WrongSubmissionPenalty: value.WrongSubmissionPenalty,
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null,
        MaxPatchAttempts: value.MaxPatchAttempts);

    public static CtfChallengeConfiguration Ctf(CtfChallengeDefinition value)
    {
        var patch = value.InteractionKind == CtfInteractionKind.PatchVerification;
        return new(
            ScoreCurve: null,
            BloodRewards: null,
            Runtime: Runtime(value.Runtime),
            InteractionKind: value.InteractionKind,
            PatchEntrypoint: patch ? value.PatchEntrypoint : null,
            PatchCommand: patch ? Strings(value, ChallengeDefinitionStringKind.PatchCommand) : null,
            PatchTimeoutSeconds: patch ? value.PatchTimeoutSeconds : null,
            Checker: patch ? Checker(value) : null,
            ReadyTimeoutSeconds: patch ? value.ReadyTimeoutSeconds : null,
            MaximumPatchUploadBytes: patch ? value.MaximumPatchUploadBytes : null,
            FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null,
            CheckerFixInput: patch && value.CheckerFixInput,
            CheckerAllowRoot: patch && value.CheckerAllowRoot);
    }

    public static AwdConfiguration Awd(AwdCompetitionModeConfiguration value) => new(
        value.HardeningDurationSeconds,
        value.RoundDurationSeconds,
        (AttackRewardMode)value.AttackRewardMode,
        value.AttackPoints,
        value.VictimDefensePoolPoints,
        value.CheckerIntervalSeconds,
        value.ServiceHealthyPoints,
        value.ServiceUnhealthyPenalty,
        Flag(value.FlagTemplate));

    public static AwdChallengeConfiguration Awd(AwdCompetitionChallengeRules value) => new(
        AttackRewardMode: value.AttackRewardMode is null
            ? null : (AttackRewardMode)value.AttackRewardMode.Value,
        AttackPoints: value.AttackPoints,
        VictimDefensePoolPoints: value.VictimDefensePoolPoints,
        CheckerIntervalSeconds: value.CheckerIntervalSeconds,
        ServiceHealthyPoints: value.ServiceHealthyPoints,
        ServiceUnhealthyPenalty: value.ServiceUnhealthyPenalty,
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null);

    public static AwdChallengeConfiguration Awd(AwdChallengeDefinition value) => new(
        Runtime: Runtime(value.Runtime),
        Checker: value.Checker is null
            ? null
            : new AwdCheckerConfiguration(
                Checker(value)!,
                value.Checker.TargetServiceName),
        FlagInjection: value.FlagInjectionCommand is null
            ? null
            : new AwdFlagInjectionConfiguration(
                value.FlagInjectionCommand,
                value.FlagInjectionTimeoutSeconds ?? 30,
                value.FlagInjectionServiceName),
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null,
        CheckerAllowRoot: value.CheckerAllowRoot);

    public static AwdpConfiguration Awdp(AwdpCompetitionModeConfiguration value) => new(
        value.RoundDurationSeconds,
        Curve(value.BreakScoreCurve),
        Curve(value.FixScoreCurve),
        value.FlagWrongPenalty,
        value.ExploitSucceededPenalty,
        value.ServiceAbnormalPenalty,
        value.RequireBreakBeforeFix,
        value.MaxBreakSubmissions,
        value.MaxFixSubmissions,
        (NoCTF.Domain.Gameplay.EvaluationDispatchMode)value.EvaluationDispatchMode,
        Flag(value.FlagTemplate));

    public static AwdpChallengeConfiguration Awdp(AwdpCompetitionChallengeRules value) => new(
        value.HasBreakScoreCurve ? Curve(value.BreakScoreCurve) : null,
        value.HasFixScoreCurve ? Curve(value.FixScoreCurve) : null,
        value.RequireBreakBeforeFix,
        value.MaxBreakSubmissions,
        value.MaxFixSubmissions,
        FlagWrongPenalty: value.FlagWrongPenalty,
        ExploitSucceededPenalty: value.ExploitSucceededPenalty,
        ServiceAbnormalPenalty: value.ServiceAbnormalPenalty,
        EvaluationDispatchMode: value.EvaluationDispatchMode is null
            ? null
            : (NoCTF.Domain.Gameplay.EvaluationDispatchMode)value.EvaluationDispatchMode.Value,
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null);

    public static AwdpChallengeConfiguration Awdp(AwdpChallengeDefinition value) => new(
        Break: null,
        Fix: null,
        RequireBreakBeforeFix: null,
        MaxBreakSubmissions: null,
        MaxFixSubmissions: null,
        Runtime: Runtime(value.Runtime),
        PatchEntrypoint: value.PatchEntrypoint,
        PatchCommand: Strings(value, ChallengeDefinitionStringKind.PatchCommand),
        PatchTimeoutSeconds: value.PatchTimeoutSeconds,
        Checker: Checker(value),
        ReadyTimeoutSeconds: value.ReadyTimeoutSeconds,
        MaximumPatchUploadBytes: value.MaximumPatchUploadBytes,
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null,
        CheckerFixInput: value.CheckerFixInput,
        CheckerAllowRoot: value.CheckerAllowRoot);

    public static KohConfiguration Koh(KohCompetitionModeConfiguration value) => new(
        value.PollIntervalSeconds,
        value.ControlPointsPerInterval);

    public static KohChallengeConfiguration Koh(KohCompetitionChallengeRules value) => new(
        PollIntervalSeconds: value.PollIntervalSeconds,
        ControlPointsPerInterval: value.ControlPointsPerInterval);

    public static KohChallengeConfiguration Koh(KohChallengeDefinition value) => new(
        Runtime(value.Runtime));

    public static ChallengeRuntimeTemplate? Runtime(ChallengeRuntimeTemplateEntity? value)
    {
        if (value is null) return null;
        var bindings = value.UrlBindings.Where(item => !item.IsControlCheck)
            .OrderBy(item => item.Position).Select(Binding).ToArray();
        var control = value.UrlBindings.Where(item => item.IsControlCheck)
            .OrderBy(item => item.Position).Select(Binding).FirstOrDefault();
        return new(
            (RuntimeAllocation)value.Allocation,
            value switch
            {
                ContainerChallengeRuntimeTemplate container => new ContainerRuntimeDefinition(
                    container.Image,
                    new ContainerSecurityPolicy(
                        container.Security.NoNewPrivileges,
                        container.Security.ReadonlyRootfs,
                        container.Security.RunAsNonRoot,
                        container.Capabilities.Where(item => !item.Add)
                            .Select(item => item.Name).ToArray(),
                        container.Capabilities.Where(item => item.Add)
                            .Select(item => item.Name).ToArray()),
                    container.CommandItems.OrderBy(item => item.Position)
                        .Select(item => item.Value).ToArray(),
                    Values(container, ChallengeRuntimeKeyValueKind.Environment),
                    Values(container, ChallengeRuntimeKeyValueKind.Label),
                    container.PortMappings.ToDictionary(
                        item => item.ContainerPort, item => item.HostPort),
                    container.FlagEnvironmentVariableName,
                    (RuntimeEgressPolicy)container.EgressPolicy,
                    container.InternalPorts.Select(item => item.Port).ToArray()),
                ComposeChallengeRuntimeTemplate compose => new ComposeRuntimeDefinition(
                    compose.ComposeYaml,
                    compose.ServiceResources.ToDictionary(
                        item => item.ServiceName,
                        item => new RuntimeResourceLimits(
                            item.Limits.MemoryBytes, item.Limits.NanoCpus, item.Limits.PidsLimit),
                        StringComparer.Ordinal),
                    Values(compose, ChallengeRuntimeKeyValueKind.Environment),
                    Values(compose, ChallengeRuntimeKeyValueKind.Label),
                    Values(compose, ChallengeRuntimeKeyValueKind.FlagEnvironmentVariable),
                    (RuntimeEgressPolicy)compose.EgressPolicy),
                OvaChallengeRuntimeTemplate ova => new OvaRuntimeDefinition(
                    ova.OvaSourceUrl, ova.Sha256),
                _ => throw new InvalidOperationException(
                    $"Unsupported Runtime template {value.GetType().Name}.")
            },
            value.HasExplicitLimits
                ? new RuntimeResourceLimits(
                    value.Limits.MemoryBytes, value.Limits.NanoCpus, value.Limits.PidsLimit)
                : null,
            value.TtlSeconds,
            value.OperationTimeoutSeconds,
            bindings,
            (RuntimeFlagSource)value.FlagSource,
            control);
    }

    public static RunnerJobConfiguration? Checker(ChallengeDefinition value) =>
        value.Checker is null
            ? null
            : new RunnerJobConfiguration(
                value.Checker.Image,
                Strings(value, ChallengeDefinitionStringKind.CheckerCommand),
                value.StringItems.Where(item =>
                        item.Kind == ChallengeDefinitionStringKind.CheckerEnvironment)
                    .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal),
                value.Checker.TimeoutSeconds);

    private static RuntimeUrlBinding Binding(ChallengeRuntimeUrlBinding value) => new(
        value.UrlTemplate,
        (RuntimeExposure)value.Exposure,
        value.ContainerPort,
        value.ServiceName,
        value.VmId,
        value.GuestPort);

    private static IReadOnlyDictionary<string, string> Values(
        ChallengeRuntimeTemplateEntity value,
        ChallengeRuntimeKeyValueKind kind) => value.KeyValues.Where(item => item.Kind == kind)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static string[] Strings(
        ChallengeDefinition value,
        ChallengeDefinitionStringKind kind) => value.StringItems.Where(item => item.Kind == kind)
            .OrderBy(item => item.Position).Select(item => item.Value).ToArray();

    private static ScoreCurveConfiguration Curve(ScoreCurveValue value) => new(
        value.InitialPoints,
        value.MinimumPoints,
        value.DecayTeamCount,
        (ScoreDecayMode)value.DecayMode,
        value.CustomExpression);

    private static PerTeamFlagTemplate Flag(FlagTemplateValue value) => new(
        value.Header,
        value.BodyTemplate,
        value.LeetLiteralText);
}

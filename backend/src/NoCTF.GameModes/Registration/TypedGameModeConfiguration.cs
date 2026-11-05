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
        Flag(value.FlagTemplate),
        value.ScoreSettlementMode);

    public static CtfChallengeConfiguration Ctf(CtfCompetitionChallengeRules value,
        NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset? preset = null) => new(
        NoCTF.Domain.Competitions.Scoring.CtfScoreCurveResolver.Override(value, preset) is { } curve ? Curve(curve) : null,
        value.BloodRewards.Count == 0 ? null : value.BloodRewards.OrderBy(item => item.Position)
            .Select(item => new BloodReward((BloodRewardPolicy)item.Policy, item.Value)).ToArray(),
        value.MaxFlagAttempts,
        WrongSubmissionPenalty: value.WrongSubmissionPenalty,
        FlagTemplate: value.HasFlagTemplate ? Flag(value.FlagTemplate) : null,
        ScoreSettlementMode: value.ScoreSettlementMode);

    public static CtfChallengeConfiguration Ctf(ChallengeDefinition value) => new(
            ScoreCurve: null,
            BloodRewards: null,
            Runtime: Runtime(value),
            FlagTemplate: value.Break is { HasFlagTemplate: true } flag ? Flag(flag.FlagTemplate) : null);

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

    public static AwdChallengeConfiguration Awd(ChallengeDefinition value) => new(
        Runtime: Runtime(value),
        Checker: value.Check is null
            ? null
            : new AwdCheckerConfiguration(
                Checker(value)!,
                value.Check.TargetServiceName),
        FlagInjection: value.Break?.InjectionCommand is null
            ? null
            : new AwdFlagInjectionConfiguration(
                value.Break.InjectionCommand,
                value.Break.InjectionTimeoutSeconds,
                value.Break.InjectionServiceName),
        FlagTemplate: value.Break is { HasFlagTemplate: true } flag ? Flag(flag.FlagTemplate) : null,
        FlagStores: value.Break?.FlagStores.Select(store => new NoCTF.Domain.Competitions.Awd.AwdFlagStore
        {
            ChallengeId = store.ChallengeId, Id = store.Id, Position = store.Position, Name = store.Name,
            TargetServiceName = store.TargetServiceName, DeliveryMode = (NoCTF.Domain.Competitions.Awd.AwdFlagDeliveryMode)store.DeliveryKind,
            InjectionCommand = store.InjectionCommand, InjectionTimeoutSeconds = store.InjectionTimeoutSeconds,
            PreservesHistoricalFlags = store.PreservesHistoricalFlags
        }).ToArray() ?? []);

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

    public static AwdpChallengeConfiguration Awdp(ChallengeDefinition value) => new(
        Break: null,
        Fix: null,
        RequireBreakBeforeFix: null,
        MaxBreakSubmissions: null,
        MaxFixSubmissions: null,
        Runtime: Runtime(value),
        PatchEntrypoint: value.Fix?.Entrypoint,
        PatchCommand: value.Fix?.Command.OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
        PatchTimeoutSeconds: value.Fix?.TimeoutSeconds,
        Checker: Checker(value),
        ReadyTimeoutSeconds: value.Check?.ReadyTimeoutSeconds,
        MaximumPatchUploadBytes: value.Fix?.MaximumUploadBytes,
        FlagTemplate: value.Break is { HasFlagTemplate: true } flag ? Flag(flag.FlagTemplate) : null,
        CheckerFixInput: value.Check?.AcceptsFixInput ?? false);

    public static KohConfiguration Koh(KohCompetitionModeConfiguration value) => new(
        value.PollIntervalSeconds,
        value.ControlPointsPerInterval);

    public static KohChallengeConfiguration Koh(KohCompetitionChallengeRules value) => new(
        PollIntervalSeconds: value.PollIntervalSeconds,
        ControlPointsPerInterval: value.ControlPointsPerInterval);

    public static KohChallengeConfiguration Koh(ChallengeDefinition value) => new(
        Runtime(value) is { } runtime && value.Control is { } control
            ? runtime with { Allocation = RuntimeAllocation.Shared, ControlCheckUrlBinding = new(control.UrlTemplate, RuntimeExposure.Participants,
                control.ContainerPort, control.ServiceName, control.VmId, control.GuestPort) }
            : Runtime(value));

    public static ChallengeRuntimeTemplate? Runtime(ChallengeDefinition? definition)
    {
        var runtime = Runtime(definition?.Runtime);
        if (runtime?.Definition is not ContainerRuntimeDefinition container || definition?.Break is not { } capability) return runtime;
        return runtime with { Definition = container with { Services = container.Services.Select(service => service with
            { FlagEnvironmentVariableName = capability.EnvironmentTargets.SingleOrDefault(target => target.ServiceName == service.Name)?.VariableName }).ToArray() } };
    }

    public static ChallengeRuntimeTemplate? Runtime(ChallengeRuntimeTemplateEntity? value)
    {
        if (value is null) return null;
        var bindings = value.UrlBindings
            .OrderBy(item => item.Position).Select(Binding).ToArray();
        return new(
            RuntimeAllocation.PerTeam,
            value switch
            {
                ContainerChallengeRuntimeTemplate container => new ContainerRuntimeDefinition(
                    container.Services.OrderBy(service => service.Position).Select(service => new RuntimeServiceDefinition(
                        service.Name, service.Image, service.CpuCores, service.MemoryMiB,
                        service.Commands.Where(item => !item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                        service.Commands.Where(item => item.IsArgument).OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                        service.Environment.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal),
                        service.InternalPorts.Select(item => item.Port).ToArray())).ToArray(),
                    (RuntimeEgressPolicy)container.EgressPolicy),
                OvaChallengeRuntimeTemplate ova => new OvaRuntimeDefinition(
                    ova.OvaSourceUrl, ova.Sha256),
                _ => throw new InvalidOperationException(
                    $"Unsupported Runtime template {value.GetType().Name}.")
            },
            value is OvaChallengeRuntimeTemplate { HasExplicitLimits: true } resources
                ? new RuntimeResourceLimits(
                    resources.Limits.MemoryBytes, resources.Limits.CpuMillicores, resources.Limits.PidsLimit)
                : null,
            null,
            value.OperationTimeoutSeconds,
            bindings,
            RuntimeFlagSource.Static,
            null);
    }

    public static ChallengeRuntimeTemplate? Runtime(ChallengeDefinition? definition, CompetitionChallengeRules rules)
    {
        var template = Runtime(definition);
        if (template is null) return null;
        if (rules is not IChallengeRuntimeRules policy) throw new InvalidOperationException("Runtime rules are required for this competition challenge.");
        var effective = template with
        {
            Allocation = (RuntimeAllocation)policy.Runtime.Allocation,
            FlagSource = (RuntimeFlagSource)policy.Runtime.FlagSource,
            TtlSeconds = policy.Runtime.TtlSeconds
        };
        return rules is KohCompetitionChallengeRules && definition?.Control is { } control
            ? effective with { ControlCheckUrlBinding = new(control.UrlTemplate, RuntimeExposure.Participants,
                control.ContainerPort, control.ServiceName, control.VmId, control.GuestPort) }
            : effective;
    }

    public static RunnerJobConfiguration? Checker(ChallengeDefinition value) =>
        value.Check is null
            ? null
            : new RunnerJobConfiguration(
                value.Check.Image,
                value.Check.Command.OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
                value.Check.Environment.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal),
                value.Check.TimeoutSeconds);

    private static RuntimeUrlBinding Binding(ChallengeRuntimeUrlBinding value) => new(
        value.UrlTemplate,
        (RuntimeExposure)value.Exposure,
        value.ContainerPort,
        value.ServiceName,
        value.VmId,
        value.GuestPort);

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

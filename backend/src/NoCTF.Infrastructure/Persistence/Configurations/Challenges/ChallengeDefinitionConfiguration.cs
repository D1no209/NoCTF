using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeDefinitionConfiguration : IEntityTypeConfiguration<ChallengeDefinition>
{
    public void Configure(EntityTypeBuilder<ChallengeDefinition> builder)
    {
        builder.ToTable("challenge_definitions");
        builder.HasKey(definition => definition.ChallengeId);
        builder.HasOne(definition => definition.Runtime)
            .WithOne()
            .HasForeignKey<ChallengeRuntimeTemplateEntity>(runtime => runtime.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Runtime).AutoInclude();
        builder.HasMany(definition => definition.Capabilities)
            .WithOne()
            .HasForeignKey(item => item.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Capabilities).AutoInclude();
    }
}

internal sealed class ChallengeRuntimeTemplateConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeTemplateEntity>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeTemplateEntity> builder)
    {
        builder.ToTable("challenge_runtime_templates");
        builder.HasKey(runtime => runtime.ChallengeId);
        builder.HasDiscriminator(runtime => runtime.RuntimeKind)
            .HasValue<ContainerChallengeRuntimeTemplate>(RuntimeKind.Container)
            .HasValue<OvaChallengeRuntimeTemplate>(RuntimeKind.OvaVm);
        builder.Property(runtime => runtime.EgressPolicy).HasConversion<short>();
        builder.HasMany(runtime => runtime.UrlBindings).WithOne()
            .HasForeignKey(binding => binding.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.UrlBindings).AutoInclude();

    }
}

internal sealed class ContainerChallengeRuntimeTemplateConfiguration : IEntityTypeConfiguration<ContainerChallengeRuntimeTemplate>
{
    public void Configure(EntityTypeBuilder<ContainerChallengeRuntimeTemplate> builder)
    {
        builder.HasMany(runtime => runtime.Services).WithOne().HasForeignKey(service => service.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.Services).AutoInclude();
    }
}

internal sealed class ChallengeRuntimeServiceConfiguration : IEntityTypeConfiguration<ChallengeRuntimeService>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeService> builder)
    {
        builder.ToTable("challenge_runtime_services");
        builder.HasKey(service => new { service.ChallengeId, service.Name });
        builder.HasIndex(service => new { service.ChallengeId, service.Position }).IsUnique();
        builder.Property(service => service.Name).HasMaxLength(63);
        builder.Property(service => service.Image).HasMaxLength(512);
        builder.Property(service => service.CpuCores).HasPrecision(12, 3);
        builder.HasMany(service => service.Commands).WithOne().HasForeignKey(item => new { item.ChallengeId, item.ServiceName }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(service => service.Environment).WithOne().HasForeignKey(item => new { item.ChallengeId, item.ServiceName }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(service => service.InternalPorts).WithOne().HasForeignKey(item => new { item.ChallengeId, item.ServiceName }).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(service => service.Commands).AutoInclude();
        builder.Navigation(service => service.Environment).AutoInclude();
        builder.Navigation(service => service.InternalPorts).AutoInclude();
    }
}

internal sealed class ChallengeRuntimeServiceCommandConfiguration : IEntityTypeConfiguration<ChallengeRuntimeServiceCommand>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeServiceCommand> builder)
    {
        builder.ToTable("challenge_runtime_service_commands");
        builder.HasKey(item => new { item.ChallengeId, item.ServiceName, item.IsArgument, item.Position });
        builder.Property(item => item.Position).ValueGeneratedNever();
        builder.Property(item => item.ServiceName).HasMaxLength(63);
    }
}

internal sealed class ChallengeRuntimeServiceEnvironmentConfiguration : IEntityTypeConfiguration<ChallengeRuntimeServiceEnvironment>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeServiceEnvironment> builder)
    {
        builder.ToTable("challenge_runtime_service_environment");
        builder.HasKey(item => new { item.ChallengeId, item.ServiceName, item.Name });
        builder.Property(item => item.ServiceName).HasMaxLength(63);
        builder.Property(item => item.Name).HasMaxLength(256);
    }
}

internal sealed class ChallengeRuntimeServicePortConfiguration : IEntityTypeConfiguration<ChallengeRuntimeServicePort>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeServicePort> builder)
    {
        builder.ToTable("challenge_runtime_service_internal_ports");
        builder.HasKey(item => new { item.ChallengeId, item.ServiceName, item.Port });
        builder.Property(item => item.ServiceName).HasMaxLength(63);
    }
}

internal sealed class ChallengeRuntimeUrlBindingConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeUrlBinding>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeUrlBinding> builder)
    {
        builder.ToTable("challenge_runtime_url_bindings");
        builder.HasKey(binding => new { binding.ChallengeId, binding.Position });
        builder.Property(binding => binding.Position).ValueGeneratedNever();
        builder.Property(binding => binding.Exposure).HasConversion<short>();
    }
}

internal sealed class OvaChallengeRuntimeTemplateConfiguration : IEntityTypeConfiguration<OvaChallengeRuntimeTemplate>
{
    public void Configure(EntityTypeBuilder<OvaChallengeRuntimeTemplate> builder) => builder.ComplexProperty(runtime => runtime.Limits);
}

internal sealed class CompetitionChallengeRulesConfiguration
    : IEntityTypeConfiguration<CompetitionChallengeRules>
{
    public void Configure(EntityTypeBuilder<CompetitionChallengeRules> builder)
    {
        builder.ToTable("competition_challenge_rules");
        builder.HasKey(rules => rules.CompetitionChallengeId);
        builder.HasDiscriminator(rules => rules.Mode)
            .HasValue<CtfCompetitionChallengeRules>(GameMode.Ctf)
            .HasValue<AwdCompetitionChallengeRules>(GameMode.Awd)
            .HasValue<AwdpCompetitionChallengeRules>(GameMode.Awdp)
            .HasValue<KohCompetitionChallengeRules>(GameMode.Koh)
            .HasValue<NoCTF.Domain.LiveSolo.LiveSoloCompetitionChallengeRules>(GameMode.LiveSolo);
        builder.Property(rules => rules.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(8);
    }
}

internal sealed class CtfChallengeRulesConfiguration : IEntityTypeConfiguration<CtfCompetitionChallengeRules>
{
    public void Configure(EntityTypeBuilder<CtfCompetitionChallengeRules> builder)
    {
        CompetitionChallengeRuntimeRulesMapping.Configure(builder, "ctf");
        builder.ComplexProperty(rules => rules.ScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        CompetitionChallengeRuntimeRulesMapping.ConfigureFlagTemplate(builder, "ctf");
        builder.HasMany(rules => rules.BloodRewards).WithOne()
            .HasForeignKey(reward => reward.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(rules => rules.BloodRewards).AutoInclude();
    }
}

internal sealed class CapabilityModeRuleFieldsConfiguration : IEntityTypeConfiguration<AwdCompetitionChallengeRules>,
    IEntityTypeConfiguration<AwdpCompetitionChallengeRules>, IEntityTypeConfiguration<NoCTF.Domain.LiveSolo.LiveSoloCompetitionChallengeRules>
{
    public void Configure(EntityTypeBuilder<AwdCompetitionChallengeRules> builder)
    {
        CompetitionChallengeRuntimeRulesMapping.Configure(builder, "awd");
        builder.Property(rules => rules.AttackRewardMode).HasConversion<short>();
        CompetitionChallengeRuntimeRulesMapping.ConfigureFlagTemplate(builder, "awd");
    }
    public void Configure(EntityTypeBuilder<AwdpCompetitionChallengeRules> builder)
    {
        CompetitionChallengeRuntimeRulesMapping.Configure(builder, "awdp");
        builder.Property(rules => rules.EvaluationDispatchMode).HasConversion<short>();
        builder.ComplexProperty(rules => rules.BreakScoreCurve, curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.ComplexProperty(rules => rules.FixScoreCurve, curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        CompetitionChallengeRuntimeRulesMapping.ConfigureFlagTemplate(builder, "awdp");
    }
    public void Configure(EntityTypeBuilder<NoCTF.Domain.LiveSolo.LiveSoloCompetitionChallengeRules> builder)
    {
        CompetitionChallengeRuntimeRulesMapping.Configure(builder, "live_solo");
        CompetitionChallengeRuntimeRulesMapping.ConfigureFlagTemplate(builder, "live_solo");
    }
}

internal static class CompetitionChallengeRuntimeRulesMapping
{
    public static void Configure<T>(EntityTypeBuilder<T> builder, string prefix) where T : CompetitionChallengeRules, IChallengeRuntimeRules =>
        builder.ComplexProperty(rules => rules.Runtime, runtime =>
        {
            runtime.Property(value => value.Allocation).HasConversion<short>().HasColumnName($"{prefix}_runtime_allocation");
            runtime.Property(value => value.FlagSource).HasConversion<short>().HasColumnName($"{prefix}_runtime_flag_source");
            runtime.Property(value => value.TtlSeconds).HasColumnName($"{prefix}_runtime_ttl_seconds");
        });

    public static void ConfigureFlagTemplate<T>(EntityTypeBuilder<T> builder, string prefix) where T : CompetitionChallengeRules, IChallengeFlagTemplateOverride =>
        builder.ComplexProperty(rules => rules.FlagTemplate, template =>
        {
            template.Property(value => value.Header).HasColumnName($"{prefix}_flag_header");
            template.Property(value => value.BodyTemplate).HasColumnName($"{prefix}_flag_body_template");
            template.Property(value => value.LeetLiteralText).HasColumnName($"{prefix}_flag_leet_literal_text");
        });
}

internal sealed class KohChallengeRuntimeRulesConfiguration : IEntityTypeConfiguration<KohCompetitionChallengeRules>
{
    public void Configure(EntityTypeBuilder<KohCompetitionChallengeRules> builder) => CompetitionChallengeRuntimeRulesMapping.Configure(builder, "koh");
}

internal sealed class CompetitionChallengeBloodRewardConfiguration
    : IEntityTypeConfiguration<CompetitionChallengeBloodReward>
{
    public void Configure(EntityTypeBuilder<CompetitionChallengeBloodReward> builder)
    {
        builder.ToTable("competition_challenge_blood_rewards");
        builder.HasKey(reward => new { reward.CompetitionChallengeId, reward.Position });
        builder.Property(reward => reward.Position).ValueGeneratedNever();
        builder.Property(reward => reward.Policy).HasConversion<short>();
        builder.Property(reward => reward.Value).HasPrecision(18, 6);
    }
}

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
        builder.HasDiscriminator(definition => definition.Mode)
            .HasValue<CtfChallengeDefinition>(GameMode.Ctf)
            .HasValue<AwdChallengeDefinition>(GameMode.Awd)
            .HasValue<AwdpChallengeDefinition>(GameMode.Awdp)
            .HasValue<KohChallengeDefinition>(GameMode.Koh)
            .HasValue<NoCTF.Domain.LiveSolo.LiveSoloChallengeDefinition>(GameMode.LiveSolo);
        builder.Property(definition => definition.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(8);
        builder.ComplexProperty(definition => definition.FlagTemplate);
        builder.HasOne(definition => definition.Runtime)
            .WithOne()
            .HasForeignKey<ChallengeRuntimeTemplateEntity>(runtime => runtime.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Runtime).AutoInclude();
        builder.HasOne(definition => definition.Checker)
            .WithOne()
            .HasForeignKey<ChallengeCheckerDefinition>(checker => checker.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.Checker).AutoInclude();
        builder.HasMany(definition => definition.StringItems)
            .WithOne()
            .HasForeignKey(item => item.ChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(definition => definition.StringItems).AutoInclude();
    }
}

internal sealed class CtfChallengeDefinitionConfiguration
    : IEntityTypeConfiguration<CtfChallengeDefinition>
{
    public void Configure(EntityTypeBuilder<CtfChallengeDefinition> builder) =>
        builder.Property(definition => definition.InteractionKind).HasConversion<short>();
}

internal sealed class ChallengeCheckerDefinitionConfiguration
    : IEntityTypeConfiguration<ChallengeCheckerDefinition>
{
    public void Configure(EntityTypeBuilder<ChallengeCheckerDefinition> builder)
    {
        builder.ToTable("challenge_checker_definitions");
        builder.HasKey(checker => checker.ChallengeId);
        builder.Property(checker => checker.Image).HasMaxLength(512);
        builder.Property(checker => checker.TargetServiceName).HasMaxLength(128);
    }
}

internal sealed class ChallengeDefinitionStringItemConfiguration
    : IEntityTypeConfiguration<ChallengeDefinitionStringItem>
{
    public void Configure(EntityTypeBuilder<ChallengeDefinitionStringItem> builder)
    {
        builder.ToTable("challenge_definition_string_items");
        builder.HasKey(item => new { item.ChallengeId, item.Kind, item.Position });
        builder.Property(item => item.Position).ValueGeneratedNever();
        builder.Property(item => item.Kind).HasConversion<short>();
        builder.Property(item => item.Key).HasMaxLength(256);
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
        builder.Property(runtime => runtime.Allocation).HasConversion<short>();
        builder.Property(runtime => runtime.FlagSource).HasConversion<short>();
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
        builder.Property(rules => rules.AttackRewardMode).HasConversion<short>();
        builder.Property(rules => rules.EvaluationDispatchMode).HasConversion<short>();
        builder.ComplexProperty(rules => rules.ScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.ComplexProperty(rules => rules.BreakScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.ComplexProperty(rules => rules.FixScoreCurve,
            curve => curve.Property(value => value.DecayMode).HasConversion<short>());
        builder.ComplexProperty(rules => rules.FlagTemplate);
        builder.HasMany(rules => rules.BloodRewards).WithOne()
            .HasForeignKey(reward => reward.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(rules => rules.BloodRewards).AutoInclude();
    }
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

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
            .HasValue<KohChallengeDefinition>(GameMode.Koh);
        builder.Property(definition => definition.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(4);
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
            .HasValue<ComposeChallengeRuntimeTemplate>(RuntimeKind.Compose)
            .HasValue<OvaChallengeRuntimeTemplate>(RuntimeKind.OvaVm);
        builder.Property(runtime => runtime.Allocation).HasConversion<short>();
        builder.Property(runtime => runtime.FlagSource).HasConversion<short>();
        builder.Property(runtime => runtime.EgressPolicy).HasConversion<short>();
        builder.ComplexProperty(runtime => runtime.Limits);
        builder.HasMany(runtime => runtime.UrlBindings).WithOne()
            .HasForeignKey(binding => binding.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.UrlBindings).AutoInclude();
        builder.HasMany(runtime => runtime.KeyValues).WithOne()
            .HasForeignKey(value => value.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.KeyValues).AutoInclude();
        builder.HasMany(runtime => runtime.CommandItems).WithOne()
            .HasForeignKey(item => item.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.CommandItems).AutoInclude();
    }
}

internal sealed class ContainerChallengeRuntimeTemplateConfiguration
    : IEntityTypeConfiguration<ContainerChallengeRuntimeTemplate>
{
    public void Configure(EntityTypeBuilder<ContainerChallengeRuntimeTemplate> builder)
    {
        builder.ComplexProperty(runtime => runtime.Security);
        builder.HasMany(runtime => runtime.Capabilities).WithOne()
            .HasForeignKey(capability => capability.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.Capabilities).AutoInclude();
        builder.HasMany(runtime => runtime.PortMappings).WithOne()
            .HasForeignKey(mapping => mapping.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.PortMappings).AutoInclude();
        builder.HasMany(runtime => runtime.InternalPorts).WithOne()
            .HasForeignKey(port => port.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.InternalPorts).AutoInclude();
    }
}

internal sealed class ComposeChallengeRuntimeTemplateConfiguration
    : IEntityTypeConfiguration<ComposeChallengeRuntimeTemplate>
{
    public void Configure(EntityTypeBuilder<ComposeChallengeRuntimeTemplate> builder)
    {
        builder.HasMany(runtime => runtime.ServiceResources).WithOne()
            .HasForeignKey(resource => resource.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(runtime => runtime.ServiceResources).AutoInclude();
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

internal sealed class ChallengeRuntimeKeyValueConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeKeyValue>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeKeyValue> builder)
    {
        builder.ToTable("challenge_runtime_key_values");
        builder.HasKey(value => new { value.ChallengeId, value.Kind, value.Key });
        builder.Property(value => value.Kind).HasConversion<short>();
    }
}

internal sealed class ChallengeRuntimeCommandItemConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeCommandItem>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeCommandItem> builder)
    {
        builder.ToTable("challenge_runtime_command_items");
        builder.HasKey(item => new { item.ChallengeId, item.Position });
        builder.Property(item => item.Position).ValueGeneratedNever();
    }
}

internal sealed class ChallengeRuntimeCapabilityConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeCapability>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeCapability> builder)
    {
        builder.ToTable("challenge_runtime_capabilities");
        builder.HasKey(capability => new { capability.ChallengeId, capability.Add, capability.Name });
    }
}

internal sealed class ChallengeRuntimePortMappingConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimePortMapping>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimePortMapping> builder)
    {
        builder.ToTable("challenge_runtime_port_mappings");
        builder.HasKey(mapping => new { mapping.ChallengeId, mapping.ContainerPort });
    }
}

internal sealed class ChallengeRuntimeInternalPortConfiguration
    : IEntityTypeConfiguration<ChallengeRuntimeInternalPort>
{
    public void Configure(EntityTypeBuilder<ChallengeRuntimeInternalPort> builder)
    {
        builder.ToTable("challenge_runtime_internal_ports");
        builder.HasKey(port => new { port.ChallengeId, port.Port });
    }
}

internal sealed class ComposeServiceResourceConfiguration
    : IEntityTypeConfiguration<ComposeServiceResource>
{
    public void Configure(EntityTypeBuilder<ComposeServiceResource> builder)
    {
        builder.ToTable("compose_service_resources");
        builder.HasKey(resource => new { resource.ChallengeId, resource.ServiceName });
        builder.ComplexProperty(resource => resource.Limits);
    }
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
            .HasValue<KohCompetitionChallengeRules>(GameMode.Koh);
        builder.Property(rules => rules.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(4);
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

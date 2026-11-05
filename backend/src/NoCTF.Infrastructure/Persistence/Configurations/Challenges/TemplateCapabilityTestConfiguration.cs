using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges.Testing;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class TemplateCapabilityTestConfiguration : IEntityTypeConfiguration<TemplateCapabilityTest>
{
    public void Configure(EntityTypeBuilder<TemplateCapabilityTest> builder)
    {
        builder.ToTable("template_capability_tests");
        builder.HasKey(test => test.RuntimeInstanceId);
        builder.HasIndex(test => test.ExecutionId).IsUnique();
        builder.HasIndex(test => new { test.State, test.Deadline });
        builder.Ignore(test => test.IsTerminal);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany().HasForeignKey(test => test.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Storage.StoredFile>().WithMany().HasForeignKey(test => test.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.OwnsMany(test => test.Requirements, requirements =>
        {
            requirements.ToTable("template_check_requirements");
            requirements.WithOwner().HasForeignKey(value => value.RuntimeInstanceId);
            requirements.HasKey(value => new { value.RuntimeInstanceId, value.Position });
            requirements.Property(value => value.Position).ValueGeneratedNever();
            requirements.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(value => value.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.OwnsMany(test => test.Probes, probes =>
        {
            probes.ToTable("template_check_probes");
            probes.WithOwner().HasForeignKey(probe => probe.RuntimeInstanceId);
            probes.HasKey(probe => new { probe.RuntimeInstanceId, probe.Position });
            probes.Property(probe => probe.Position).ValueGeneratedNever();
            probes.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(probe => probe.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.OwnsMany(test => test.FlagContexts, contexts =>
        {
            contexts.ToTable("template_check_flag_contexts");
            contexts.WithOwner().HasForeignKey(value => value.RuntimeInstanceId);
            contexts.HasKey(value => new { value.RuntimeInstanceId, value.FlagId });
            contexts.HasOne<NoCTF.Domain.Challenges.ChallengeFlag>().WithMany().HasForeignKey(value => value.FlagId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

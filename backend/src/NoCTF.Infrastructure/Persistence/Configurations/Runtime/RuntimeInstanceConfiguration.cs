using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeInstanceConfiguration : IEntityTypeConfiguration<RuntimeInstance>
{
    public void Configure(EntityTypeBuilder<RuntimeInstance> builder)
    {
        builder.ToTable("runtime_instances", table =>
        {
            table.HasCheckConstraint(
                "ck_runtime_instances_failure",
                "(state = 5) = (failure_code IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_runtime_instances_checker_sequence",
                "last_applied_checker_sequence <= checker_sequence");
        });
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.RuntimeKind).HasConversion<short>();
        builder.Property(instance => instance.RuntimeProvider).HasConversion<short>();
        builder.Property(instance => instance.State).HasConversion<short>();
        builder.Property(instance => instance.FailureCode).HasConversion<short>();
        builder.Property(instance => instance.ProcessingVersion).IsConcurrencyToken();
        builder.Property(instance => instance.ProviderReceiptJson).HasColumnType("jsonb");
        builder.Property(instance => instance.Urls).HasColumnType("text[]");
        builder.Property(instance => instance.ParticipantUrlIndexes).HasColumnType("integer[]");
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId,
            instance.Generation
        }).IsUnique();
        builder.HasIndex(instance => new
        {
            instance.CompetitionChallengeId,
            instance.TeamId
        }).IsUnique().HasFilter("state IN (0, 1, 2)");
        builder.HasIndex(instance => new
        {
            instance.RunnerPool,
            instance.State,
            instance.CreatedAt,
            instance.Id
        });
        builder.HasIndex(instance => new { instance.State, instance.NextCheckerDueAt })
            .HasFilter("next_checker_due_at IS NOT NULL");
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(instance => instance.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(instance => instance.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(instance => instance.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuntimeInstance>().WithMany()
            .HasForeignKey(instance => instance.ReplacesRuntimeInstanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

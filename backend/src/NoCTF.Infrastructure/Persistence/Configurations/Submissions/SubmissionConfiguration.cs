using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Submissions;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FlagHash).HasMaxLength(64);
        builder.Property(x => x.LegacyFlag).HasColumnName("Flag");
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.ProcessingVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompetitionId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompetitionId, x.TeamId, x.ChallengeId, x.Kind, x.ReceivedAt, x.Id });
        builder.HasIndex(x => x.ScoringEventId).IsUnique();
        builder.HasOne(x => x.ScoringEvent).WithMany().HasForeignKey(x => x.ScoringEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

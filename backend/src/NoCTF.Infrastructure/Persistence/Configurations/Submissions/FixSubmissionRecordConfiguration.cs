using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Submissions;

internal sealed class FixSubmissionRecordConfiguration : IEntityTypeConfiguration<FixSubmissionRecord>
{
    public void Configure(EntityTypeBuilder<FixSubmissionRecord> builder)
    {
        builder.ToTable("fix_submission_records");
        builder.HasKey(x => x.UploadId);
        builder.Property(x => x.ObjectKey).HasMaxLength(512);
        builder.Property(x => x.ObjectMetadata).HasColumnType("jsonb");
        builder.Property(x => x.VerifierVersion).HasMaxLength(128);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.SubmissionId).IsUnique();
        builder.HasIndex(x => new { x.CompetitionId, x.TeamId, x.CompetitionChallengeId, x.ExpiresAt });
        builder.HasOne(x => x.Submission).WithMany().HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

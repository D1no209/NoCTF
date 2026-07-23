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
        builder.Property(x => x.Kind).HasConversion<short>();
        builder.Property(x => x.EvaluationState).HasConversion<short>();
        builder.Property(x => x.EvaluationFailureCode).HasConversion<short>();
        builder.Property(x => x.ProcessingVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompetitionId, x.TeamId, x.CompetitionChallengeId, x.Kind, x.ReceivedAt, x.Id });
        builder.HasIndex(x => new { x.CompetitionId, x.ReceivedAt, x.Id });
        builder.HasIndex(x => x.PatchUploadId).IsUnique();
        builder.HasIndex(x => x.CurrentScoringEventId).IsUnique();
        builder.HasOne<ScoringEvent>().WithMany().HasForeignKey(x => x.CurrentScoringEventId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(submission => submission.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(submission => submission.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(submission => submission.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(submission => submission.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PatchUpload>().WithOne()
            .HasForeignKey<Submission>(submission => submission.PatchUploadId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_submissions_payload",
            "(kind IN (0, 1) AND submitted_flag IS NOT NULL AND submitted_flag_sha256 IS NOT NULL AND patch_upload_id IS NULL) OR (kind = 2 AND submitted_flag IS NULL AND submitted_flag_sha256 IS NULL AND patch_upload_id IS NOT NULL)"));
    }
}

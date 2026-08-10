using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.Persistence.Configurations.GameplayFacts;

internal sealed class PatchUploadConfiguration : IEntityTypeConfiguration<PatchUpload>
{
    public void Configure(EntityTypeBuilder<PatchUpload> builder)
    {
        builder.ToTable("patch_uploads");
        builder.HasKey(upload => upload.Id);
        builder.HasIndex(upload => upload.FileId).IsUnique();
        builder.HasIndex(upload => new { upload.TeamId, upload.CompetitionChallengeId });
        builder.HasOne<NoCTF.Domain.Competitions.Competition>().WithMany()
            .HasForeignKey(upload => upload.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>().WithMany()
            .HasForeignKey(upload => upload.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(upload => upload.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Identity.User>().WithMany()
            .HasForeignKey(upload => upload.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(upload => upload.File).WithMany()
            .HasForeignKey(upload => upload.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}

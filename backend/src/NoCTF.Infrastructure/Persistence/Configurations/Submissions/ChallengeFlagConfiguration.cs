using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Submissions;

internal sealed class ChallengeFlagConfiguration : IEntityTypeConfiguration<ChallengeFlag>
{
    public void Configure(EntityTypeBuilder<ChallengeFlag> builder)
    {
        builder.ToTable("challenge_flags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Flag).HasColumnType("text");
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new
        {
            x.CompetitionId, x.CompetitionChallengeId, x.TeamId, x.StageId, x.ChallengeInstanceId,
            x.ValidStart, x.ValidEnd
        });
        builder.HasIndex(x => new { x.CompetitionId, x.CompetitionChallengeId, x.TeamId, x.ValidStart })
            .IsUnique()
            .HasFilter("\"TeamId\" IS NOT NULL AND \"StageId\" IS NULL");
        builder.HasOne<CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(flag => flag.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

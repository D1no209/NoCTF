using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.GameplayFacts;

internal sealed class ChallengeFlagConfiguration : IEntityTypeConfiguration<ChallengeFlag>
{
    public void Configure(EntityTypeBuilder<ChallengeFlag> builder)
    {
        builder.ToTable("challenge_flags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Flag).HasColumnType("text");
        builder.Property(x => x.MatchKind).HasConversion<short>();
        builder.Property(x => x.SpecificationKind).HasConversion<short>();
        builder.HasIndex(x => new
        {
            x.CompetitionChallengeId, x.TeamId, x.SpecificationKind, x.SpecificationId
        });
        builder.HasIndex(x => new { x.SpecificationKind, x.SpecificationId })
            .IsUnique()
            .HasFilter("specification_kind = 4 AND deleted_at IS NULL");
        builder.HasIndex(x => new { x.ChallengeId, x.FlagSha256 })
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => new { x.CompetitionChallengeId, x.FlagSha256 })
            .HasFilter("deleted_at IS NULL");
        builder.HasOne<CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(flag => flag.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(flag => flag.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>().WithMany()
            .HasForeignKey(flag => flag.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => x.DeletedAt == null);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_challenge_flags_scope",
                "(challenge_id IS NULL) <> (competition_challenge_id IS NULL)");
            table.HasCheckConstraint(
                "ck_challenge_flags_specification",
                "(specification_kind IS NULL) = (specification_id IS NULL)");
        });
    }
}

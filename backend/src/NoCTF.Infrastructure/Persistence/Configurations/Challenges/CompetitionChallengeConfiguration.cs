using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class CompetitionChallengeConfiguration : IEntityTypeConfiguration<CompetitionChallenge>
{
    public void Configure(EntityTypeBuilder<CompetitionChallenge> builder)
    {
        builder.ToTable("competition_challenges");
        builder.HasKey(item => item.Id);
        // PostgreSQL jsonb is required for versioned, mode-specific competition rules.
        builder.Property(item => item.RulesJson).HasColumnType("jsonb");
        builder.HasQueryFilter(item => item.DeletedAt == null);
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(item => item.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.CompetitionId, item.Order }).IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(item => new { item.CompetitionId, item.ChallengeId }).IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasMany(item => item.Hints)
            .WithOne()
            .HasForeignKey(hint => hint.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompetitionChallengeHintConfiguration
    : IEntityTypeConfiguration<CompetitionChallengeHint>
{
    public void Configure(EntityTypeBuilder<CompetitionChallengeHint> builder)
    {
        builder.ToTable("competition_challenge_hints");
        builder.HasKey(hint => hint.Id);
    }
}

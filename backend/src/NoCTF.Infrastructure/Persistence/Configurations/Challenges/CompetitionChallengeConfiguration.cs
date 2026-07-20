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
        builder.Property(item => item.ConfigurationJson).HasColumnType("jsonb");
        builder.OwnsOne(item => item.Deletion);
        builder.HasQueryFilter(item => !item.Deletion.IsDeleted);
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(item => item.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(item => item.ChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.CompetitionId, item.Order }).IsUnique();
        builder.HasIndex(item => new { item.CompetitionId, item.ChallengeId }).IsUnique();
        builder.OwnsMany(item => item.Hints, hints =>
        {
            hints.ToTable("competition_challenge_hints");
            hints.WithOwner().HasForeignKey("CompetitionChallengeId");
            hints.HasKey(hint => hint.Id);
        });
    }
}

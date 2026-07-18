using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeModeConfiguration : IEntityTypeConfiguration<ChallengeConfiguration>
{
    public void Configure(EntityTypeBuilder<ChallengeConfiguration> builder)
    {
        builder.ToTable("challenge_configurations");
        builder.HasKey(configuration => configuration.ChallengeId);
        builder.Property(configuration => configuration.Json).HasColumnType("jsonb");
        builder.HasOne<Challenge>().WithOne().HasForeignKey<ChallengeConfiguration>(configuration => configuration.ChallengeId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeSourceConfiguration : IEntityTypeConfiguration<ChallengeSource>
{
    public void Configure(EntityTypeBuilder<ChallengeSource> builder)
    {
        builder.HasOne<Challenge>().WithOne().HasForeignKey<ChallengeSource>(source => source.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(source => source.CanonicalChallengeId);
    }
}

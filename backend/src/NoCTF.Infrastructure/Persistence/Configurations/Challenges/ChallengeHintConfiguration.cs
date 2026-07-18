using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeHintConfiguration : IEntityTypeConfiguration<ChallengeHint>
{
    public void Configure(EntityTypeBuilder<ChallengeHint> builder)
    {
        builder.ToTable("challenge_hints");
        builder.HasKey(hint => hint.Id);
        builder.HasIndex(hint => hint.ChallengeId);
    }
}

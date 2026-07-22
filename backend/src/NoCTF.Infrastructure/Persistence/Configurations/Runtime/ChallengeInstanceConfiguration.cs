using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class ChallengeInstanceConfiguration : IEntityTypeConfiguration<ChallengeInstance>
{
    public void Configure(EntityTypeBuilder<ChallengeInstance> builder)
    {
        builder.ToTable("challenge_instances");
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.Receipt).HasColumnType("jsonb");
        builder.HasIndex(instance => new { instance.CompetitionId, instance.TeamId, instance.CompetitionChallengeId });
        builder.HasOne<NoCTF.Domain.Challenges.CompetitionChallenge>()
            .WithMany()
            .HasForeignKey(instance => instance.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

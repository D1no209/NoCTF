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
        builder.HasDiscriminator(x => x.Type)
            .HasValue<TemplateChallengeFlag>(ChallengeFlagType.Template)
            .HasValue<CompetitionChallengeFlag>(ChallengeFlagType.Competition)
            .HasValue<TeamChallengeFlag>(ChallengeFlagType.Team)
            .HasValue<AwdRoundChallengeFlag>(ChallengeFlagType.AwdRound)
            .HasValue<RuntimeInstanceChallengeFlag>(ChallengeFlagType.RuntimeInstance);
        builder.Property(x => x.MatchKind).HasConversion<short>();
        builder.Property(x => x.SpecificationKind).HasConversion<short>();
        builder.Property(x => x.SpecificationIdentity).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => x.SpecificationIdentity).IsUnique();
        builder.HasIndex(x => new { x.ChallengeId, x.FlagSha256 });
        builder.HasIndex(x => new { x.CompetitionChallengeId, x.FlagSha256 });
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
    }
}

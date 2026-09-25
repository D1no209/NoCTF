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
        builder.HasDiscriminator(item => item.Mode)
            .HasValue<CtfCompetitionChallenge>(GameMode.Ctf)
            .HasValue<AwdCompetitionChallenge>(GameMode.Awd)
            .HasValue<AwdpCompetitionChallenge>(GameMode.Awdp)
            .HasValue<KohCompetitionChallenge>(GameMode.Koh);
        builder.Property(item => item.Mode)
            .HasConversion<GameModeStringConverter>()
            .HasMaxLength(4);
        builder.HasOne(item => item.Rules)
            .WithOne()
            .HasForeignKey<CompetitionChallengeRules>(rules => rules.CompetitionChallengeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Rules).AutoInclude();
        builder.HasQueryFilter(item => item.DeletedAt == null);
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(item => new { item.CompetitionId, item.Mode })
            .HasPrincipalKey(competition => new { competition.Id, competition.Mode })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>()
            .WithMany()
            .HasForeignKey(item => new { item.ChallengeId, item.Mode })
            .HasPrincipalKey(challenge => new { challenge.Id, challenge.Mode })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.CompetitionId, item.Order }).IsUnique();
        builder.HasIndex(item => item.NormalizedCustomTitle);
        builder.HasIndex(item => new { item.CompetitionId, item.ChallengeId }).IsUnique();
        builder.OwnsMany(item => item.Hints, hints =>
        {
            hints.ToTable("competition_challenge_hints");
            hints.WithOwner().HasForeignKey("competition_challenge_id");
            hints.HasKey(hint => hint.Id);
            hints.Property(hint => hint.Content).IsRequired();
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.WriteUps;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.Configurations.Challenges;

internal sealed class ChallengeWriteUpConfiguration : IEntityTypeConfiguration<ChallengeWriteUp>,
    IEntityTypeConfiguration<ChallengeWriteUpVersion>, IEntityTypeConfiguration<WriteUpUnlockReceipt>
{
    public void Configure(EntityTypeBuilder<ChallengeWriteUp> builder)
    {
        builder.ToTable("competition_challenge_writeups");
        builder.HasIndex(x => new { x.CompetitionChallengeId, x.Source, x.AuthorScopeId }).IsUnique();
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>().WithMany().HasForeignKey(x => x.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.WriteUpId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<ChallengeWriteUpVersion> builder)
    {
        builder.ToTable("competition_challenge_writeup_versions");
        builder.HasIndex(x => new { x.WriteUpId, x.Number }).IsUnique();
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<WriteUpUnlockReceipt> builder)
    {
        builder.ToTable("competition_challenge_writeup_unlocks");
        builder.HasIndex(x => new { x.TeamId, x.CompetitionChallengeId }).IsUnique();
        builder.HasOne<GameplayFact>().WithOne().HasForeignKey<WriteUpUnlockReceipt>(x => x.GameplayFactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CompetitionChallenge>().WithMany().HasForeignKey(x => x.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChallengeWriteUpVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

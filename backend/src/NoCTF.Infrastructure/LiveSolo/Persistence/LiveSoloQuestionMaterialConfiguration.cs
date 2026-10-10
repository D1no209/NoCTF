using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Persistence;

internal sealed class LiveSoloQuestionMaterialConfiguration : IEntityTypeConfiguration<LiveSoloQuestionMaterial>
{
    public void Configure(EntityTypeBuilder<LiveSoloQuestionMaterial> builder)
    {
        builder.ToTable("live_solo_question_materials");
        builder.HasIndex(x => x.ChallengeId).IsUnique();
        builder.HasIndex(x => new { x.CompetitionId, x.SourceTemplateId }).IsUnique();
        builder.HasOne<CompetitionChallenge>().WithOne().HasForeignKey<LiveSoloQuestionMaterial>(x => x.CompetitionChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Competition>().WithMany().HasForeignKey(x => x.CompetitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>().WithMany().HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Challenge>().WithMany().HasForeignKey(x => x.SourceTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

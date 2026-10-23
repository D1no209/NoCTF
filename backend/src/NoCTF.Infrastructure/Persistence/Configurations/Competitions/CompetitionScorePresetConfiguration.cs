using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Scoring;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionScorePresetConfiguration : IEntityTypeConfiguration<CompetitionScorePreset>
{
    public void Configure(EntityTypeBuilder<CompetitionScorePreset> builder)
    {
        builder.ToTable("competition_score_presets");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.CompetitionId, item.NormalizedName }).IsUnique();
        builder.HasOne<Competition>().WithMany().HasForeignKey(item => item.CompetitionId).OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(item => item.ScoreCurve);
        builder.Navigation(item => item.ScoreCurve).IsRequired();
    }
}

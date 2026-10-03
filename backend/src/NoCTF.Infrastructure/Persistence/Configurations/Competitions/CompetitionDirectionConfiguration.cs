using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Directions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionDirectionConfiguration : IEntityTypeConfiguration<CompetitionDirection>
{
    public void Configure(EntityTypeBuilder<CompetitionDirection> builder)
    {
        builder.ToTable("competition_directions");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.CompetitionId, item.NormalizedName }).IsUnique();
        builder.HasAlternateKey(item => new { item.CompetitionId, item.Id });
        builder.HasOne<Competition>().WithMany(item => item.Directions)
            .HasForeignKey(item => item.CompetitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

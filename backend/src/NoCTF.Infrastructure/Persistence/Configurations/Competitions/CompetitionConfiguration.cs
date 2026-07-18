using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionEntityConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(competition => competition.Id);
        builder.Property(competition => competition.Title).HasMaxLength(160);
        builder.OwnsOne(competition => competition.Deletion);
        builder.HasIndex(competition => new { competition.Status, competition.StartTime });
    }
}

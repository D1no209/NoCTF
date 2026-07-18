using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionModeConfiguration : IEntityTypeConfiguration<CompetitionConfiguration>
{
    public void Configure(EntityTypeBuilder<CompetitionConfiguration> builder)
    {
        builder.ToTable("competition_configurations");
        builder.HasKey(configuration => configuration.CompetitionId);
        builder.Property(configuration => configuration.Json).HasColumnType("jsonb");
        builder.HasOne<Competition>().WithOne().HasForeignKey<CompetitionConfiguration>(configuration => configuration.CompetitionId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Platform;

namespace NoCTF.Infrastructure.Persistence.Configurations.Platform;

internal sealed class PlatformSettingsConfiguration
    : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");

        // Data annotations cannot seed the singleton platform configuration row.
        builder.HasData(new PlatformSettings
        {
            Id = 1,
            Name = "NoCTF",
            Description = null,
            LogoObjectKey = null,
            Revision = 1,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });
    }
}

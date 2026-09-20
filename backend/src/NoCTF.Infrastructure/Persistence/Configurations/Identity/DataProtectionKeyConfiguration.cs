using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NoCTF.Infrastructure.Persistence.Configurations.Identity;

internal sealed class DataProtectionKeyConfiguration
    : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.Property(key => key.FriendlyName).HasMaxLength(1024);
        builder.Property(key => key.Xml).IsRequired();
    }
}

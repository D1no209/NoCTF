using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeCapacityLedgerConfiguration
    : IEntityTypeConfiguration<RuntimeCapacityLedger>
{
    public void Configure(EntityTypeBuilder<RuntimeCapacityLedger> builder)
    {
        builder.ToTable("runtime_capacity_ledger");
        builder.HasData(new RuntimeCapacityLedger
        {
            Id = 1,
            ConcurrencyStamp = Guid.Parse("00000000-0000-0000-0000-000000000002")
        });
    }
}

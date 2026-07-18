using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeOperationConfiguration : IEntityTypeConfiguration<RuntimeOperation>
{
    public void Configure(EntityTypeBuilder<RuntimeOperation> builder)
    {
        builder.ToTable("runtime_operations");
        builder.HasKey(operation => operation.Id);
        builder.HasIndex(operation => new { operation.CompetitionId, operation.Status });
    }
}

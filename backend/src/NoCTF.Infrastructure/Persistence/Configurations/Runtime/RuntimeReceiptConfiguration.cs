using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Runtime;

namespace NoCTF.Infrastructure.Persistence.Configurations.Runtime;

internal sealed class RuntimeReceiptConfiguration : IEntityTypeConfiguration<RuntimeReceipt>
{
    public void Configure(EntityTypeBuilder<RuntimeReceipt> builder)
    {
        builder.ToTable("runtime_receipts");
        builder.HasKey(receipt => receipt.RuntimeInstanceId);
        builder.Property(receipt => receipt.Provider).HasConversion<short>();
        var discriminator = builder.HasDiscriminator<string>("receipt_type");
        foreach (var entry in NoCTF.Generated.PersistentDiscriminatorCatalog.Entries
                     .Where(entry => entry.Hierarchy == typeof(RuntimeReceipt)))
            discriminator.HasValue(entry.Leaf, entry.Value);

        builder.OwnsMany(receipt => receipt.Services, services =>
        {
            services.ToTable("runtime_receipt_services");
            services.WithOwner().HasForeignKey("runtime_instance_id");
            services.HasKey(service => service.Id);
            services.HasIndex("runtime_instance_id", nameof(ContainerRuntimeReceiptService.Name)).IsUnique();
            services.OwnsMany(service => service.PublishedPorts, ports =>
            {
                ports.ToTable("runtime_receipt_service_ports");
                ports.WithOwner().HasForeignKey("service_id");
                ports.HasKey(port => port.Id);
                ports.HasIndex("service_id", nameof(ContainerRuntimeReceiptPort.ContainerPort)).IsUnique();
            });
        });
        builder.OwnsMany(receipt => receipt.VirtualMachines, machines =>
        {
            machines.ToTable("runtime_receipt_virtual_machines");
            machines.WithOwner().HasForeignKey("runtime_instance_id");
            machines.HasKey(machine => machine.Id);
        });
    }
}

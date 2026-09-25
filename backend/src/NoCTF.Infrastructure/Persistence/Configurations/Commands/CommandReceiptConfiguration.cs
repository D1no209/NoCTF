using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Commands;

namespace NoCTF.Infrastructure.Persistence.Configurations.Commands;

internal sealed class CommandReceiptConfiguration : IEntityTypeConfiguration<CommandReceipt>
{
    public void Configure(EntityTypeBuilder<CommandReceipt> builder)
    {
        builder.ToTable("command_receipts");
        builder.HasKey(receipt => receipt.Id);
        var discriminator = builder.HasDiscriminator(receipt => receipt.Operation);
        foreach (var entry in CommandReceiptGeneratedCatalog.Entries)
            discriminator.HasValue(entry.Leaf, entry.Kind);
        builder.Property(receipt => receipt.InputFingerprint).HasMaxLength(32);
        builder.Property(receipt => receipt.RuntimeState).HasConversion<short>();
        builder.Property(receipt => receipt.GameplayFactState).HasConversion<short>();
        builder.HasIndex(receipt => new
        {
            receipt.UserId,
            receipt.Operation,
            receipt.CompetitionId,
            receipt.ResourceId
        });
        builder.HasMany(receipt => receipt.GameplayFactResults)
            .WithOne()
            .HasForeignKey(result => result.CommandReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(receipt => receipt.GameplayFactResults).AutoInclude();
    }
}

internal sealed class CommandReceiptGameplayFactResultConfiguration
    : IEntityTypeConfiguration<CommandReceiptGameplayFactResult>
{
    public void Configure(EntityTypeBuilder<CommandReceiptGameplayFactResult> builder)
    {
        builder.ToTable("command_receipt_gameplay_fact_results");
        builder.HasKey(result => new { result.CommandReceiptId, result.Position });
        builder.Property(result => result.Position).ValueGeneratedNever();
    }
}

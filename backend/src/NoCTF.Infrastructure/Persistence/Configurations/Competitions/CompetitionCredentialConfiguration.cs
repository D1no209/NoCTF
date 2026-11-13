using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Competitions.Credentials;

namespace NoCTF.Infrastructure.Persistence.Configurations.Competitions;

internal sealed class CompetitionCredentialBatchConfiguration
    : IEntityTypeConfiguration<CompetitionCredentialBatch>
{
    public void Configure(EntityTypeBuilder<CompetitionCredentialBatch> builder)
    {
        builder.ToTable("competition_credential_batches");
        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.SourceFileName).HasMaxLength(255);
        builder.Property(batch => batch.SourceSha256).HasMaxLength(64);
        builder.HasIndex(batch => new { batch.CompetitionId, batch.CreatedAt });
        builder.HasMany(batch => batch.Credentials)
            .WithOne(credential => credential.Batch)
            .HasForeignKey(credential => credential.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompetitionCredentialConfiguration
    : IEntityTypeConfiguration<CompetitionCredential>
{
    public void Configure(EntityTypeBuilder<CompetitionCredential> builder)
    {
        builder.ToTable("competition_credentials");
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.PasswordCiphertext).IsRequired();
        builder.HasIndex(credential => new { credential.BatchId, credential.UserId }).IsUnique();
        builder.HasIndex(credential => new { credential.CompetitionId, credential.UserId });
        builder.HasOne<NoCTF.Domain.Identity.User>()
            .WithMany()
            .HasForeignKey(credential => credential.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NoCTF.Domain.Teams.Team>()
            .WithMany()
            .HasForeignKey(credential => credential.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

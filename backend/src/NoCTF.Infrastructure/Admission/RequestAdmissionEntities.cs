using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Admission;

public sealed class RequestAdmissionWindow : IConcurrencyTracked
{
    [MaxLength(64)] public string KeyHash { get; set; } = string.Empty;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int Count { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class RequestAdmissionLease : IConcurrencyTracked
{
    [MaxLength(64)] public string KeyHash { get; set; } = string.Empty;
    public Guid LeaseId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class RequestAdmissionWindowConfiguration
    : IEntityTypeConfiguration<RequestAdmissionWindow>
{
    public void Configure(EntityTypeBuilder<RequestAdmissionWindow> builder)
    {
        builder.ToTable("request_admission_windows");
        builder.HasKey(item => item.KeyHash);
        builder.HasIndex(item => item.ExpiresAt);
    }
}

internal sealed class RequestAdmissionLeaseConfiguration
    : IEntityTypeConfiguration<RequestAdmissionLease>
{
    public void Configure(EntityTypeBuilder<RequestAdmissionLease> builder)
    {
        builder.ToTable("request_admission_leases");
        builder.HasKey(item => new { item.KeyHash, item.LeaseId });
        builder.HasIndex(item => item.ExpiresAt);
        builder.HasIndex(item => item.LeaseId);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoCTF.Application.Authentication.Sso;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Authentication;

public sealed class SsoFlowEntity : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public SsoProtocol Protocol { get; set; }
    public SsoFlowIntent Intent { get; set; }
    [MaxLength(128)] public string BrowserIdHash { get; set; } = string.Empty;
    public SsoFlowState State { get; set; }
    [MaxLength(64)] public string CorrelationHash { get; set; } = string.Empty;
    [MaxLength(256)] public string ProviderFingerprint { get; set; } = string.Empty;
    [MaxLength(2048)] public string ReturnPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? UserId { get; set; }
    public int? TokenVersion { get; set; }
    [MaxLength(2048)] public string? Nonce { get; set; }
    [MaxLength(2048)] public string? PkceVerifier { get; set; }
    [MaxLength(2048)] public string? ServiceUrl { get; set; }
    [MaxLength(64)] public string? ProcessingTokenHash { get; set; }
    public Guid? ExternalProviderId { get; set; }
    public SsoProtocol? ExternalProtocol { get; set; }
    [MaxLength(512)] public string? ExternalNamespace { get; set; }
    [MaxLength(255)] public string? ExternalSubject { get; set; }
    [MaxLength(512)] public string? ExternalDisplayName { get; set; }
    public SsoFailureCode? FailureCode { get; set; }
}

internal sealed class SsoFlowEntityConfiguration : IEntityTypeConfiguration<SsoFlowEntity>
{
    public void Configure(EntityTypeBuilder<SsoFlowEntity> builder)
    {
        builder.ToTable("sso_flows");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.CorrelationHash).IsUnique();
        builder.HasIndex(item => item.ExpiresAt);
    }
}

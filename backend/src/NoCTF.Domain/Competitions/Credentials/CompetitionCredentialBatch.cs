using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Credentials;

public sealed class CompetitionCredentialBatch : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CompetitionCredential> Credentials { get; set; } = [];
}

public sealed class CompetitionCredential : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }
    public CompetitionCredentialBatch? Batch { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public Guid TeamId { get; set; }
    public byte[] PasswordCiphertext { get; set; } = [];
    public int PasswordVersion { get; set; } = 1;
    public DateTimeOffset PasswordUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

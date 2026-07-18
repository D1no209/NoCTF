using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Teams;

/// <summary>Represents a competition-scoped team.</summary>
public sealed class Team
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid CaptainId { get; set; }
    public bool IsLocked { get; set; }
    public TeamRegistrationStatus RegistrationStatus { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public TeamBanState Ban { get; set; } = new();
    public SoftDeleteState Deletion { get; set; } = new();
}

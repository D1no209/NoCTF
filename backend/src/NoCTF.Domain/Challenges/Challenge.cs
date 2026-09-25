using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges;

/// <summary>Represents a reusable challenge template for exactly one game mode.</summary>
[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(GameMode), "Challenge")]
public abstract class Challenge : IConcurrencyTracked
{
    protected Challenge(GameMode mode) => Mode = mode;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public List<ChallengeManager> Managers { get; set; } = [];
    [NotMapped]
    public Guid[] ManagerIds
    {
        get => Managers.Select(manager => manager.UserId).ToArray();
        set
        {
            var desired = (value ?? []).ToHashSet();
            Managers.RemoveAll(manager => !desired.Contains(manager.UserId));
            var existing = Managers.Select(manager => manager.UserId).ToHashSet();
            Managers.AddRange(desired.Where(userId => !existing.Contains(userId))
                .Select(userId => new ChallengeManager { ChallengeId = Id, UserId = userId }));
        }
    }
    public GameMode Mode { get; private set; }
    public ChallengeVisibility Visibility { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(160)]
    public string NormalizedTitle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = "Uncategorized";
    [MaxLength(96)]
    public string NormalizedDirection { get; set; } = "UNCATEGORIZED";
    public ChallengeDefinition? Definition { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<ChallengeAttachment> Attachments { get; set; } = [];
}

public sealed class ChallengeManager
{
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
}

public enum ChallengeVisibility : short
{
    Private,
    Shared
}

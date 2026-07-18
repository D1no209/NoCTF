namespace NoCTF.Domain.Competitions;

public enum CompetitionCollaboratorRole
{
    Manager,
    Observer
}

/// <summary>Grants a user access to administer or observe one competition.</summary>
public sealed class CompetitionCollaborator
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public CompetitionCollaboratorRole Role { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
